
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// The main window: the same streams the menu bar shows, as a browsable
// list with transport controls. Closing it leaves the app in the menu bar.

import AppKit

final class MainWindowController: NSWindowController, NSOutlineViewDataSource,
                                  NSOutlineViewDelegate, NSWindowDelegate {

    private let player: Player

    /// Asked for a fresh streams tree when the window needs one.
    private let reload: () -> Result<StreamNode, Error>

    private var root = StreamNode(group: "")
    private var loadError: String?

    private let outlineView = NSOutlineView()
    private let statusLabel = NSTextField(labelWithString: "")
    private let playButton = NSButton()
    private let muteButton = NSButton()
    private let volumeSlider = NSSlider()

    init(player: Player, reload: @escaping () -> Result<StreamNode, Error>) {
        self.player = player
        self.reload = reload

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 520),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )

        window.title = "GaGa"
        window.setFrameAutosaveName("GaGaMainWindow")
        window.center()

        super.init(window: window)

        window.delegate = self
        window.contentView = buildContentView()

        volumeSlider.doubleValue = player.volume * 100
        refresh()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not used")
    }

    //
    // Layout
    //

    private func buildContentView() -> NSView {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("stream"))
        column.title = "Streams"

        outlineView.addTableColumn(column)
        outlineView.outlineTableColumn = column
        outlineView.headerView = nil
        outlineView.dataSource = self
        outlineView.delegate = self
        outlineView.rowSizeStyle = .default
        outlineView.style = .sourceList
        outlineView.target = self
        outlineView.doubleAction = #selector(onOutlineDoubleClick)

        let scrollView = NSScrollView()
        scrollView.documentView = outlineView
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.borderType = .noBorder

        statusLabel.lineBreakMode = .byTruncatingTail
        statusLabel.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        statusLabel.textColor = .secondaryLabelColor

        playButton.bezelStyle = .texturedRounded
        playButton.target = self
        playButton.action = #selector(onPlayClicked)

        muteButton.bezelStyle = .texturedRounded
        muteButton.target = self
        muteButton.action = #selector(onMuteClicked)

        volumeSlider.minValue = 0
        volumeSlider.maxValue = 100
        volumeSlider.isContinuous = true
        volumeSlider.target = self
        volumeSlider.action = #selector(onVolumeChanged)
        volumeSlider.widthAnchor.constraint(equalToConstant: 110).isActive = true

        let controls = NSStackView(views: [playButton, muteButton, volumeSlider, statusLabel])
        controls.orientation = .horizontal
        controls.spacing = 8
        controls.edgeInsets = NSEdgeInsets(top: 8, left: 12, bottom: 10, right: 12)
        controls.setHuggingPriority(.defaultLow, for: .horizontal)

        let separator = NSBox()
        separator.boxType = .separator

        let stack = NSStackView(views: [scrollView, separator, controls])
        stack.orientation = .vertical
        stack.spacing = 0
        stack.distribution = .fill

        return stack
    }

    //
    // Contents
    //

    /// Reload the streams tree and the transport controls.
    func refresh() {
        switch reload() {
        case .success(let root):
            self.root = root
            loadError = nil
        case .failure(let error):
            root = StreamNode(group: "")
            loadError = error.localizedDescription
        }

        outlineView.reloadData()
        outlineView.expandItem(nil, expandChildren: true)
        refreshControls()
    }

    /// Update only the transport controls, for player state changes.
    func refreshControls() {
        playButton.image = NSImage(
            systemSymbolName: player.isIdle ? "play.fill" : "stop.fill",
            accessibilityDescription: player.isIdle ? "Play" : "Stop"
        )

        muteButton.image = NSImage(
            systemSymbolName: player.isMuted ? "speaker.slash.fill" : "speaker.wave.2.fill",
            accessibilityDescription: player.isMuted ? "Unmute" : "Mute"
        )

        playButton.isEnabled = player.source != nil
        muteButton.isEnabled = !player.isIdle

        if let error = loadError {
            statusLabel.stringValue = error
        } else if let source = player.source {
            let state = player.isIdle ? "Idle" : (player.isMuted ? "Playing (muted)" : "Playing")
            statusLabel.stringValue = "\(state) - \(source.name)"
        } else {
            statusLabel.stringValue = "No stream selected"
        }
    }

    func show() {
        refresh()
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    //
    // Outline view
    //

    private func node(_ item: Any?) -> StreamNode {
        item as? StreamNode ?? root
    }

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        node(item).children.count
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        node(item).children[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        node(item).isGroup
    }

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?,
                     item: Any) -> NSView? {
        let node = node(item)
        let identifier = NSUserInterfaceItemIdentifier("cell")

        let view = outlineView.makeView(withIdentifier: identifier, owner: self) as? NSTableCellView
            ?? {
                let created = NSTableCellView()
                let field = NSTextField(labelWithString: "")
                field.translatesAutoresizingMaskIntoConstraints = false

                created.identifier = identifier
                created.addSubview(field)
                created.textField = field

                NSLayoutConstraint.activate([
                    field.leadingAnchor.constraint(equalTo: created.leadingAnchor),
                    field.trailingAnchor.constraint(equalTo: created.trailingAnchor),
                    field.centerYAnchor.constraint(equalTo: created.centerYAnchor),
                ])

                return created
            }()

        view.textField?.stringValue = node.name
        view.textField?.font = node.isGroup
            ? .boldSystemFont(ofSize: NSFont.systemFontSize)
            : .systemFont(ofSize: NSFont.systemFontSize)

        return view
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        !node(item).isGroup
    }

    //
    // Events
    //

    @objc private func onOutlineDoubleClick() {
        guard
            outlineView.clickedRow >= 0,
            let node = outlineView.item(atRow: outlineView.clickedRow) as? StreamNode,
            let stream = node.stream
        else { return }

        player.play(stream)
    }

    @objc private func onPlayClicked() {
        // play what is selected, if anything, otherwise the current stream:
        if player.isIdle,
           let node = outlineView.item(atRow: outlineView.selectedRow) as? StreamNode,
           let stream = node.stream {
            player.play(stream)
        } else {
            player.togglePlay()
        }
    }

    @objc private func onMuteClicked() {
        player.toggleMute()
    }

    @objc private func onVolumeChanged() {
        player.volume = volumeSlider.doubleValue / 100
    }

    /// Closing the window leaves the app running in the menu bar.
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        window?.orderOut(nil)
        return false
    }
}
