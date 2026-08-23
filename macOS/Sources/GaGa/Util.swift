
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import AppKit

/// Where the settings and the streams file live.
enum AppPaths {

    static let folder: URL = {
        let support = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        )[0]

        return support.appendingPathComponent("GaGa", isDirectory: true)
    }()

    static let settings = folder.appendingPathComponent("Settings.json")
    static let streams = folder.appendingPathComponent("Streams.ini")

    /// The stream played by default on a fresh install: a local playlist
    /// in iCloud Drive, so that editing it there changes what plays.
    static let defaultStream = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs/capris.m3u")
}

extension String {

    /// Text read from a streams file or a playlist: UTF-8 when possible,
    /// Latin-1 as a fallback so that a bad encoding is not fatal, and never
    /// with a byte order mark left at the front. Editors on Windows write
    /// one routinely, and it would otherwise end up glued to the first line.
    init(textData data: Data) {
        let text = String(data: data, encoding: .utf8)
            ?? String(data: data, encoding: .isoLatin1)
            ?? ""

        self = text.hasPrefix("\u{FEFF}") ? String(text.dropFirst()) : text
    }
}

/// Modal messages. This is an accessory app with no windows of its own,
/// so it must come to the front before showing one.
enum Alert {

    static func show(title: String, message: String) {
        NSApp.activate(ignoringOtherApps: true)

        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.runModal()
    }

    /// Returns true when the user picks the confirm button.
    static func confirm(title: String, message: String, confirmTitle: String) -> Bool {
        NSApp.activate(ignoringOtherApps: true)

        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: confirmTitle)
        alert.addButton(withTitle: "Cancel")

        return alert.runModal() == .alertFirstButtonReturn
    }
}

/// A labeled volume slider, used as the view of a menu item.
final class VolumeSliderView: NSView {

    private let label = NSTextField(labelWithString: "Volume")
    private let slider = NSSlider()

    /// Called with a value between 0 and 1 while the slider moves.
    var onChange: ((Double) -> Void)?

    var value: Double {
        get { slider.doubleValue / 100 }
        set {
            slider.doubleValue = min(max(newValue, 0), 1) * 100
            updateLabel()
        }
    }

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: 220, height: 48))

        label.font = .menuFont(ofSize: NSFont.systemFontSize)
        label.textColor = .labelColor

        slider.minValue = 0
        slider.maxValue = 100
        slider.target = self
        slider.action = #selector(onSliderChanged)
        slider.isContinuous = true

        let stack = NSStackView(views: [label, slider])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 4
        stack.edgeInsets = NSEdgeInsets(top: 6, left: 14, bottom: 8, right: 14)
        stack.translatesAutoresizingMaskIntoConstraints = false

        addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
            slider.widthAnchor.constraint(equalToConstant: 180),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not used")
    }

    @objc private func onSliderChanged() {
        updateLabel()
        onChange?(value)
    }

    private func updateLabel() {
        label.stringValue = "Volume  \(Int((value * 100).rounded()))"
    }
}
