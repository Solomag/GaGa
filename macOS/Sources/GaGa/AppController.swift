
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import AppKit

final class AppController: NSObject, NSApplicationDelegate, NSMenuDelegate {

    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let menu = NSMenu()

    private var settings = Settings()
    private let loader = StreamsFileLoader(filePath: AppPaths.streams.path)
    private lazy var player = Player(statusItem: statusItem)
    private let power = PowerWatcher()
    private var windowController: MainWindowController?

    /// The last streams tree read from disk, reused until the file changes.
    private var streams: Result<StreamNode, Error>?

    // constant menu items:
    private lazy var showWindowItem = menuItem("Open GaGa Window", #selector(onShowWindowClicked))
    private lazy var editItem = menuItem("Edit Streams File", #selector(onEditClicked))
    private lazy var quitItem = menuItem("Quit GaGa", #selector(onQuitClicked), key: "q")

    // audio submenu:
    private let volumeSlider = VolumeSliderView()
    private lazy var audioItem = NSMenuItem(title: "Audio", action: nil, keyEquivalent: "")

    // options submenu:
    private lazy var optionsItem = NSMenuItem(title: "Options", action: nil, keyEquivalent: "")
    private lazy var autoPlayItem = menuItem("Play on Startup", #selector(onAutoPlayClicked))
    private lazy var mediaKeysItem = menuItem("Enable Media Keys", #selector(onMediaKeysClicked))

    //
    // Startup and shutdown
    //

    func applicationDidFinishLaunching(_ notification: Notification) {
        settings = loadSettings()

        menu.delegate = self
        menu.autoenablesItems = false

        buildMainMenu()
        buildAudioMenu()
        buildOptionsMenu()

        statusItem.button?.action = #selector(onStatusItemClicked)
        statusItem.button?.target = self
        statusItem.button?.sendAction(on: [.leftMouseUp, .rightMouseUp, .otherMouseUp])

        // apply settings:
        volumeSlider.value = settings.volume
        player.volume = settings.volume
        player.select(settings.lastStream)

        autoPlayItem.state = settings.autoPlay ? .on : .off
        mediaKeysItem.state = settings.mediaKeys ? .on : .off
        player.setMediaKeysEnabled(settings.mediaKeys)

        let window = MainWindowController(player: player) { [weak self] in
            self?.currentStreams() ?? .success(StreamNode(group: ""))
        }

        windowController = window

        player.onStateChanged = { [weak self] in
            self?.windowController?.refreshControls()
            self?.power.setMonitoring(self?.player.needsPowerWatch ?? false)
        }

        window.show()

        // stop for a closing lid or a sleeping machine, and come back after:
        power.onSuspend = { [weak self] in self?.player.suspend() }
        power.onResume = { [weak self] in self?.player.resume() }
        power.start()

        if settings.autoPlay {
            player.play()
        }

        power.setMonitoring(player.needsPowerWatch)
    }

    /// Closing the window leaves the app in the menu bar.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    /// Clicking the dock icon brings the window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool {
        windowController?.show()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        player.stop()
        saveSettings()
    }

    private func loadSettings() -> Settings {
        do {
            return try Settings.load(from: AppPaths.settings)
        } catch {
            Alert.show(
                title: "Error loading settings file",
                message: """
                    Unable to load settings:
                    \(AppPaths.settings.path)

                    This usually means the file is corrupt, empty or \
                    incompatible with the current GaGa version. Defaults \
                    will be used instead.

                    \(error.localizedDescription)
                    """
            )
            return Settings()
        }
    }

    private func saveSettings() {
        settings.volume = player.volume
        settings.lastStream = player.source
        settings.autoPlay = autoPlayItem.state == .on
        settings.mediaKeys = mediaKeysItem.state == .on

        do {
            try settings.save(to: AppPaths.settings)
        } catch {
            Alert.show(
                title: "Error saving settings file",
                message: "Unable to save settings:\n\(AppPaths.settings.path)"
                    + "\n\n\(error.localizedDescription)"
            )
        }
    }

    /// The streams file, re-read only when it changed on disk. Shared by
    /// the menu and the window, so that both show the same tree.
    private func currentStreams() -> Result<StreamNode, Error> {
        if streams == nil || loader.mustReload() {
            streams = Result { try loader.load() }
        }

        return streams ?? .success(StreamNode(group: ""))
    }

    //
    // Menus
    //

    private func menuItem(_ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        return item
    }

    /// The menu bar of the application itself, shown while it is in front.
    private func buildMainMenu() {
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About GaGa",
                        action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)),
                        keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(menuItem("Edit Streams File", #selector(onEditClicked)))
        appMenu.addItem(menuItem("Reload Streams", #selector(onReloadClicked), key: "r"))
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide GaGa",
                        action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit GaGa",
                        action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        let appItem = NSMenuItem()
        appItem.submenu = appMenu

        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Minimize",
                           action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Close",
                           action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")

        let windowItem = NSMenuItem()
        windowItem.submenu = windowMenu

        let mainMenu = NSMenu()
        mainMenu.addItem(appItem)
        mainMenu.addItem(windowItem)

        NSApp.mainMenu = mainMenu
        NSApp.windowsMenu = windowMenu
    }

    private func buildAudioMenu() {
        volumeSlider.onChange = { [weak self] value in
            self?.player.volume = value
            self?.windowController?.refreshControls()
        }

        let volumeItem = NSMenuItem()
        volumeItem.view = volumeSlider

        let submenu = NSMenu(title: "Audio")
        submenu.autoenablesItems = false
        submenu.addItem(volumeItem)

        audioItem.submenu = submenu
    }

    private func buildOptionsMenu() {
        let submenu = NSMenu(title: "Options")
        submenu.autoenablesItems = false
        submenu.addItem(autoPlayItem)
        submenu.addItem(mediaKeysItem)

        optionsItem.submenu = submenu
    }

    /// Rebuild the status item menu from the streams file. Called every time
    /// it is about to open, so that edits to the file show up right away.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        volumeSlider.value = player.volume

        var canEdit = true

        switch currentStreams() {
        case .success(let root):
            menu.addStreams(root, target: self, action: #selector(onStreamClicked(_:)))

        case .failure(let error as StreamsFileReadError):
            let item = menuItem(
                "Error reading streams file (click for details)",
                #selector(onReadErrorClicked(_:))
            )
            item.representedObject = error
            menu.addItem(item)

        case .failure(let error):
            let item = menuItem(
                "Error opening streams file (click for details)",
                #selector(onOpenErrorClicked(_:))
            )
            item.representedObject = error.localizedDescription
            menu.addItem(item)

            // there is no file to edit:
            canEdit = false
        }

        editItem.isEnabled = canEdit

        menu.addItem(.separator())
        menu.addItem(showWindowItem)
        menu.addItem(editItem)
        menu.addItem(.separator())
        menu.addItem(audioItem)
        menu.addItem(optionsItem)
        menu.addItem(.separator())
        menu.addItem(quitItem)

        // the window shows the same streams:
        windowController?.refresh()
    }

    private func showMenu() {
        statusItem.menu = menu
        statusItem.button?.performClick(nil)
        statusItem.menu = nil
    }

    //
    // Events: status item
    //

    /// Left click toggles play, middle click toggles mute, right click
    /// (or control click) opens the menu. Same as the Windows version.
    @objc private func onStatusItemClicked() {
        guard let event = NSApp.currentEvent else {
            showMenu()
            return
        }

        switch event.type {
        case .leftMouseUp where !event.modifierFlags.contains(.control):
            if player.source == nil {
                showMenu()
            } else {
                player.togglePlay()
            }

        case .otherMouseUp:
            if player.isIdle {
                showMenu()
            } else {
                player.toggleMute()
            }

        default:
            showMenu()
        }
    }

    //
    // Events: menu
    //

    @objc private func onStreamClicked(_ sender: NSMenuItem) {
        guard let stream = sender.representedObject as? PlayerStream else { return }
        player.play(stream)
    }

    @objc private func onShowWindowClicked() {
        windowController?.show()
    }

    @objc private func onReloadClicked() {
        streams = nil
        windowController?.refresh()
    }

    @objc private func onEditClicked() {
        openStreamsFile()
    }

    /// Open the streams file with the default application for .ini files.
    private func openStreamsFile() {
        guard !NSWorkspace.shared.open(AppPaths.streams) else { return }

        Alert.show(
            title: "Error opening streams file",
            message: """
                No application is set to open .ini files:
                \(AppPaths.streams.path)

                Pick one with File > Get Info in Finder, or open the file \
                by hand with any text editor.
                """
        )
    }

    @objc private func onOpenErrorClicked(_ sender: NSMenuItem) {
        let message = sender.representedObject as? String ?? "Unknown error."
        Alert.show(title: "Error opening streams file", message: message)
    }

    @objc private func onReadErrorClicked(_ sender: NSMenuItem) {
        guard let error = sender.representedObject as? StreamsFileReadError else { return }

        let confirmed = Alert.confirm(
            title: "Error reading streams file",
            message: """
                \(error.filePath)
                Error at line \(error.lineNumber): \(error.message)

                \(error.line)

                Do you want to open the streams file now?
                """,
            confirmTitle: "Open"
        )

        if confirmed {
            openStreamsFile()
        }
    }

    @objc private func onAutoPlayClicked() {
        autoPlayItem.state = autoPlayItem.state == .on ? .off : .on
    }

    @objc private func onMediaKeysClicked() {
        mediaKeysItem.state = mediaKeysItem.state == .on ? .off : .on
        player.setMediaKeysEnabled(mediaKeysItem.state == .on)
    }

    @objc private func onQuitClicked() {
        NSApp.terminate(nil)
    }
}
