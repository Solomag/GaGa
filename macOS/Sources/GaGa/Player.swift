
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import AVFoundation
import AppKit
import MediaPlayer

/// A named media stream.
struct PlayerStream: Codable, Equatable {
    let name: String
    let url: URL
}

/// A media player that takes control of a status item image and tooltip
/// to display status.
final class Player: NSObject {

    private let statusItem: NSStatusItem
    private let player = AVPlayer()

    private(set) var source: PlayerStream?
    private(set) var isIdle = true

    /// The address actually playing, after unwrapping playlists.
    private(set) var streamURL: URL?

    /// Records the current stream on request. Stops with the stream.
    let recorder = StreamRecorder()

    /// Bumped on every play/stop, so that a playlist resolution or a
    /// player event belonging to a previous stream is ignored.
    private var generation = 0

    /// True when the machine, not the user, stopped playback.
    private(set) var isSuspended = false

    /// Whether a lid or sleep change would have anything to act on: either
    /// something is playing, or something is waiting to be started again.
    var needsPowerWatch: Bool { !isIdle || isSuspended }

    private var timeControlObservation: NSKeyValueObservation?
    private var itemStatusObservation: NSKeyValueObservation?

    private var bufferingTimer: Timer?
    private var currentBufferingIcon = 0

    /// Player volume, between 0 and 1.
    var volume: Double = 0.3 {
        didSet { player.volume = Float(min(max(volume, 0), 1)) }
    }

    var isMuted: Bool { player.isMuted }

    /// True while the stream is opening or has stalled, as opposed to
    /// actually feeding audio out.
    var isBuffering: Bool { !isIdle && player.timeControlStatus != .playing }

    /// Called whenever the state changes, so that the window can follow.
    var onStateChanged: (() -> Void)?

    private let idleIcon = Player.icon("radio", "Idle")
    private let playingIcon = Player.icon("antenna.radiowaves.left.and.right", "Playing")
    private let mutedIcon = Player.icon("speaker.slash.fill", "Muted")

    private let bufferingIcons = [
        Player.icon("speaker.fill", "Buffering"),
        Player.icon("speaker.wave.1.fill", "Buffering"),
        Player.icon("speaker.wave.2.fill", "Buffering"),
        Player.icon("speaker.wave.3.fill", "Buffering"),
    ]

    init(statusItem: NSStatusItem) {
        self.statusItem = statusItem
        super.init()

        player.volume = Float(volume)
        player.actionAtItemEnd = .pause

        timeControlObservation = player.observe(\.timeControlStatus, options: [.new]) {
            [weak self] player, _ in
            DispatchQueue.main.async { self?.onTimeControlStatusChanged(player.timeControlStatus) }
        }

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(onItemDidPlayToEnd(_:)),
            name: .AVPlayerItemDidPlayToEndTime,
            object: nil
        )

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(onItemFailedToPlayToEnd(_:)),
            name: .AVPlayerItemFailedToPlayToEndTime,
            object: nil
        )

        setUpRemoteCommands()
        updateIcon()
    }

    //
    // Icon handling
    //

    private static func icon(_ symbol: String, _ description: String) -> NSImage? {
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: description)
        image?.isTemplate = true
        return image
    }

    /// Update the status item image and tooltip for the current state.
    private func updateIcon() {
        let image: NSImage?
        var text: String

        // player state:
        if isIdle {
            image = idleIcon
            text = "Idle"
        } else if player.isMuted {
            image = mutedIcon
            text = "Playing (muted)"
        } else {
            image = playingIcon
            text = "Playing"
        }

        // source state:
        text += " - " + (source?.name ?? "No stream selected")

        statusItem.button?.image = image
        statusItem.button?.toolTip = text

        onStateChanged?()
    }

    //
    // Playing
    //

    /// Open and play the current source stream. Unmutes the player.
    func play() {
        guard let source else { return }

        // playing again by hand cancels any pending automatic resume:
        isSuspended = false
        generation += 1
        let token = generation

        // silence whatever is playing right now: resolving a playlist takes
        // a round trip, and until it lands nothing of the previous station
        // should still be heard under the new one's name.
        player.pause()
        player.replaceCurrentItem(with: nil)
        itemStatusObservation = nil
        recorder.stop()
        streamURL = nil

        isIdle = false
        player.isMuted = false
        updateIcon()

        // resolving a playlist is part of getting the stream started:
        startBufferingAnimation()

        PlaylistResolver.resolve(source.url) { [weak self] result in
            guard let self, self.generation == token else { return }

            switch result {
            case .success(let url):
                self.startPlaying(url, token: token)
            case .failure(let error):
                self.fail(error.localizedDescription)
            }
        }
    }

    /// Set a given stream as current and play it. Unmutes the player.
    func play(_ stream: PlayerStream) {
        source = stream
        play()
    }

    /// Stop playing and set a given stream as current, without playing it.
    func select(_ stream: PlayerStream?) {
        stop()
        source = stream
        updateIcon()
    }

    /// Stop playing and close the current stream. Unmutes the player.
    func stop() {
        guard !isIdle else { return }

        generation += 1

        // pause() alone keeps downloading from online streams:
        player.pause()
        player.replaceCurrentItem(with: nil)
        player.isMuted = false

        itemStatusObservation = nil
        recorder.stop()
        streamURL = nil
        stopBufferingAnimation()

        isIdle = true
        updateNowPlaying()
        updateIcon()
    }

    func togglePlay() {
        isIdle ? play() : stop()
    }

    /// Stop because the machine is going away: sleeping, or the lid closing.
    /// A live stream cannot be paused and picked up later, so this stops it
    /// and remembers to open it again.
    func suspend() {
        guard !isIdle else { return }

        // before stopping, so that the state change is reported with the
        // suspension already in effect:
        isSuspended = true
        stop()
    }

    /// Start again, but only if we were the ones who stopped.
    func resume() {
        guard isSuspended else { return }
        play()
    }

    func mute() {
        guard !isIdle, !player.isMuted else { return }
        player.isMuted = true
        updateIcon()
    }

    func unMute() {
        guard !isIdle, player.isMuted else { return }
        player.isMuted = false
        updateIcon()
    }

    func toggleMute() {
        player.isMuted ? unMute() : mute()
    }

    private func startPlaying(_ url: URL, token: Int) {
        streamURL = url
        let item = AVPlayerItem(url: url)

        itemStatusObservation = item.observe(\.status, options: [.new]) {
            [weak self] item, _ in
            DispatchQueue.main.async {
                guard let self, self.generation == token, item.status == .failed else { return }
                self.fail(item.error?.localizedDescription ?? "Unknown playback error.")
            }
        }

        player.replaceCurrentItem(with: item)
        player.volume = Float(volume)
        player.play()

        updateNowPlaying()
    }

    /// Stop playing and report why.
    private func fail(_ message: String) {
        let name = source?.name ?? "stream"
        let url = source?.url.absoluteString ?? ""

        stop()
        Alert.show(title: "Unable to play: \(name)", message: "\(message)\n\n\(url)")
    }

    //
    // Buffering animation
    //

    private func startBufferingAnimation() {
        guard bufferingTimer == nil else { return }

        let timer = Timer(timeInterval: 0.3, repeats: true) { [weak self] _ in
            self?.onBufferingTick()
        }

        // keep animating while a menu is open:
        RunLoop.main.add(timer, forMode: .common)
        bufferingTimer = timer
    }

    private func stopBufferingAnimation() {
        bufferingTimer?.invalidate()
        bufferingTimer = nil
        currentBufferingIcon = 0
    }

    private func onBufferingTick() {
        // the mute icon has priority over the buffering icons:
        if !player.isMuted {
            statusItem.button?.image = bufferingIcons[currentBufferingIcon]
        }

        currentBufferingIcon = (currentBufferingIcon + 1) % bufferingIcons.count
    }

    //
    // Player events
    //

    private func onTimeControlStatusChanged(_ status: AVPlayer.TimeControlStatus) {
        guard !isIdle else { return }

        switch status {
        case .waitingToPlayAtSpecifiedRate:
            startBufferingAnimation()
            updateIcon()
        case .playing:
            stopBufferingAnimation()
            updateIcon()
        default:
            break
        }
    }

    @objc private func onItemDidPlayToEnd(_ notification: Notification) {
        guard notification.object as AnyObject === player.currentItem else { return }
        stop()
    }

    @objc private func onItemFailedToPlayToEnd(_ notification: Notification) {
        guard notification.object as AnyObject === player.currentItem else { return }

        let error = notification.userInfo?[AVPlayerItemFailedToPlayToEndTimeErrorKey] as? Error
        fail(error?.localizedDescription ?? "The stream stopped unexpectedly.")
    }

    //
    // Media keys and Now Playing
    //

    /// Enable or disable the media keys, which reach us through the
    /// system Now Playing controls. No accessibility permission needed.
    func setMediaKeysEnabled(_ enabled: Bool) {
        let center = MPRemoteCommandCenter.shared()

        center.togglePlayPauseCommand.isEnabled = enabled
        center.playCommand.isEnabled = enabled
        center.pauseCommand.isEnabled = enabled
        center.stopCommand.isEnabled = enabled
    }

    private func setUpRemoteCommands() {
        let center = MPRemoteCommandCenter.shared()

        center.togglePlayPauseCommand.addTarget { [weak self] _ in
            self?.togglePlay()
            return .success
        }

        center.playCommand.addTarget { [weak self] _ in
            self?.play()
            return .success
        }

        center.pauseCommand.addTarget { [weak self] _ in
            self?.stop()
            return .success
        }

        center.stopCommand.addTarget { [weak self] _ in
            self?.stop()
            return .success
        }

        // commands we don't implement, so that the system doesn't
        // offer them in the Now Playing controls:
        center.nextTrackCommand.isEnabled = false
        center.previousTrackCommand.isEnabled = false
        center.changePlaybackPositionCommand.isEnabled = false
    }

    private func updateNowPlaying() {
        let center = MPNowPlayingInfoCenter.default()

        guard !isIdle, let source else {
            center.nowPlayingInfo = nil
            center.playbackState = .stopped
            return
        }

        center.nowPlayingInfo = [
            MPMediaItemPropertyTitle: source.name,
            MPMediaItemPropertyArtist: "GaGa",
            MPNowPlayingInfoPropertyIsLiveStream: true,
            MPNowPlayingInfoPropertyPlaybackRate: 1.0,
        ]

        center.playbackState = .playing
    }
}
