
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// Playing a radio stream should not keep the machine awake, and should not
// keep running into a closed lid. This watches for both.

import AppKit
import CoreAudio
import IOKit

final class PowerWatcher {

    /// Playback should stop: the machine is going to sleep, or the lid
    /// has just been closed.
    var onSuspend: (() -> Void)?

    /// It can start again.
    var onResume: (() -> Void)?

    /// Waking takes a moment to bring the network back, and a live stream
    /// opened too early just fails.
    private let resumeDelay: TimeInterval = 3

    private var rootDomain: io_service_t = IO_OBJECT_NULL
    private var notificationPort: IONotificationPortRef?
    private var notification: io_object_t = IO_OBJECT_NULL
    private var lidIsClosed = false
    private var lidTimer: Timer?

    /// False when the lid watch could not be armed, in which case the
    /// display notifications above are the only trigger.
    private(set) var isWatchingLid = false

    deinit {
        stop()
    }

    func start() {
        Self.allowIdleSleepDuringAudio()
        watchSleepAndWake()
        watchLid()
    }

    /// Watch the lid only while there is something to act on. The IOKit
    /// message below is the fast path, but whether it arrives for a clamshell
    /// change is not something we can count on, so the state is also sampled
    /// while playing. Reading it is a register lookup, and the tolerance lets
    /// the system coalesce the wakeups.
    func setMonitoring(_ monitoring: Bool) {
        guard monitoring != (lidTimer != nil) else { return }

        guard monitoring else {
            lidTimer?.invalidate()
            lidTimer = nil
            return
        }

        let timer = Timer(timeInterval: 2, repeats: true) { [weak self] _ in
            self?.checkLid()
        }

        timer.tolerance = 1
        RunLoop.main.add(timer, forMode: .common)
        lidTimer = timer
    }

    func stop() {
        setMonitoring(false)
        NSWorkspace.shared.notificationCenter.removeObserver(self)

        if notification != IO_OBJECT_NULL {
            IOObjectRelease(notification)
            notification = IO_OBJECT_NULL
        }

        if let notificationPort {
            IONotificationPortDestroy(notificationPort)
            self.notificationPort = nil
        }

        if rootDomain != IO_OBJECT_NULL {
            IOObjectRelease(rootDomain)
            rootDomain = IO_OBJECT_NULL
        }
    }

    //
    // Idle sleep
    //

    /// By default CoreAudio holds a PreventUserIdleSystemSleep assertion for
    /// any process with audio playing, which would keep the machine awake for
    /// as long as the radio is on. A radio player has no business doing that.
    private static func allowIdleSleepDuringAudio() {
        var allowed: UInt32 = 1
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertySleepingIsAllowed,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )

        AudioObjectSetPropertyData(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            UInt32(MemoryLayout<UInt32>.size),
            &allowed
        )
    }

    //
    // Sleep and wake
    //

    private func watchSleepAndWake() {
        let center = NSWorkspace.shared.notificationCenter

        center.addObserver(
            self,
            selector: #selector(onWillSleep),
            name: NSWorkspace.willSleepNotification,
            object: nil
        )

        center.addObserver(
            self,
            selector: #selector(onDidWake),
            name: NSWorkspace.didWakeNotification,
            object: nil
        )

        // closing the lid always sleeps the displays, so these are a second
        // trigger for the lid check below, independent of IOKit messages
        // arriving. They only act when the clamshell state really changed,
        // so an ordinary display timeout does not stop the radio.
        center.addObserver(
            self,
            selector: #selector(onScreensChanged),
            name: NSWorkspace.screensDidSleepNotification,
            object: nil
        )

        center.addObserver(
            self,
            selector: #selector(onScreensChanged),
            name: NSWorkspace.screensDidWakeNotification,
            object: nil
        )
    }

    @objc private func onScreensChanged() {
        checkLid()
    }

    @objc private func onWillSleep() {
        onSuspend?()
    }

    @objc private func onDidWake() {
        // the lid may still be closed on a wake for something else:
        guard !readLidIsClosed() else { return }
        resumeSoon()
    }

    //
    // Lid
    //

    /// Closing the lid does not always put the machine to sleep: on power,
    /// or with an external display, it just keeps going. Watch the clamshell
    /// state directly rather than inferring it from sleep.
    private func watchLid() {
        rootDomain = IOServiceGetMatchingService(
            kIOMainPortDefault,
            IOServiceMatching("IOPMrootDomain")
        )

        guard rootDomain != IO_OBJECT_NULL else { return }

        lidIsClosed = readLidIsClosed()

        notificationPort = IONotificationPortCreate(kIOMainPortDefault)
        guard let notificationPort else { return }

        IONotificationPortSetDispatchQueue(notificationPort, .main)

        let context = Unmanaged.passUnretained(self).toOpaque()

        // any power management message can carry a clamshell change, so
        // compare the state itself instead of matching message types:
        let callback: IOServiceInterestCallback = { context, _, _, _ in
            guard let context else { return }
            Unmanaged<PowerWatcher>.fromOpaque(context)
                .takeUnretainedValue()
                .checkLid()
        }

        let result = IOServiceAddInterestNotification(
            notificationPort,
            rootDomain,
            kIOGeneralInterest,
            callback,
            context,
            &notification
        )

        isWatchingLid = result == kIOReturnSuccess
    }

    private func readLidIsClosed() -> Bool {
        guard rootDomain != IO_OBJECT_NULL else { return false }

        let value = IORegistryEntryCreateCFProperty(
            rootDomain,
            "AppleClamshellState" as CFString,
            kCFAllocatorDefault,
            0
        )?.takeRetainedValue()

        return value as? Bool ?? false
    }

    private func checkLid() {
        let closed = readLidIsClosed()
        guard closed != lidIsClosed else { return }

        lidIsClosed = closed

        if closed {
            onSuspend?()
        } else {
            resumeSoon()
        }
    }

    private func resumeSoon() {
        DispatchQueue.main.asyncAfter(deadline: .now() + resumeDelay) { [weak self] in
            guard let self else { return }

            // the lid can close again inside the delay, and coming back
            // then would start the radio into a shut laptop:
            guard !self.readLidIsClosed() else { return }

            self.onResume?()
        }
    }
}
