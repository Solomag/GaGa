// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// Favorites, recording, start at login and updates.

import AppKit
import ServiceManagement

extension AppController {

    func buildRecordingMenu() {
        let submenu = NSMenu(title: "Recording")
        submenu.autoenablesItems = false
        submenu.addItem(recordToggleItem)
        submenu.addItem(recordFolderItem)

        recordingItem.submenu = submenu
    }

    /// Enable, check and label the items that depend on the player state.
    func updateItemStates() {
        let current = player.source
        let known = current.map { StreamsFileWriter.contains(AppPaths.streams, stream: $0.url) } ?? false

        favoriteItem.isEnabled = current != nil && !known
        favoriteItem.title = known ? "In Your Streams File" : "Add to Favorites"

        let recording = player.recorder.isRecording
        recordToggleItem.isEnabled = recording || (!player.isIdle && isRecordable(player.streamURL))
        recordToggleItem.state = recording ? .on : .off
        recordToggleItem.title = recording ? "Recording (Click to Stop)" : "Record This Station"
        recordingItem.state = recording ? .on : .off

        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
    }

    private func isRecordable(_ url: URL?) -> Bool {
        guard let scheme = url?.scheme?.lowercased() else { return false }

        // HLS (.m3u8) is a list of segments, not one stream to save:
        return (scheme == "http" || scheme == "https") && url?.pathExtension.lowercased() != "m3u8"
    }

    //
    // Favorites
    //

    @objc func onFavoriteClicked() {
        guard let current = player.source else { return }

        do {
            try StreamsFileWriter.addStream(
                AppPaths.streams,
                section: favoritesSection,
                name: current.name,
                stream: current.url,
                newSectionFirst: true
            )
        } catch {
            Alert.show(title: "Unable to add to favorites", message: error.localizedDescription)
        }
    }

    //
    // Recording
    //

    /// "~/Music/GaGa Recordings/Station 2026-11-13 21-05-00", without extension.
    private func recordingBasePath(for stream: PlayerStream) -> URL {
        var name = stream.name
            .components(separatedBy: CharacterSet(charactersIn: "/:\\"))
            .joined(separator: "_")
            .trimmingCharacters(in: .whitespaces)

        if name.isEmpty || name.hasPrefix(".") { name = "Recording " + name }
        if name.count > 80 { name = String(name.prefix(80)) }

        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH-mm-ss"

        return AppPaths.recordings.appendingPathComponent(name + " " + formatter.string(from: Date()))
    }

    @objc func onRecordClicked() {
        let recorder = player.recorder

        if recorder.isRecording {
            let file = recorder.fileURL
            recorder.stop()

            if let file {
                NSWorkspace.shared.activateFileViewerSelecting([file])
            }
            return
        }

        guard let source = player.source, !player.isIdle,
              let url = player.streamURL, isRecordable(url)
        else {
            Alert.show(title: "Unable to record", message: "Start playing an http:// or https:// station first.")
            return
        }

        recorder.start(url, basePath: recordingBasePath(for: source))
    }

    @objc func onRecordFolderClicked() {
        do {
            try FileManager.default.createDirectory(at: AppPaths.recordings, withIntermediateDirectories: true)
            NSWorkspace.shared.open(AppPaths.recordings)
        } catch {
            Alert.show(title: "Error opening recordings folder", message: error.localizedDescription)
        }
    }

    //
    // Start at login
    //

    @objc func onLoginItemClicked() {
        let service = SMAppService.mainApp

        do {
            if service.status == .enabled {
                try service.unregister()
            } else {
                try service.register()
            }
        } catch {
            Alert.show(
                title: "Unable to change Start at Login",
                message: error.localizedDescription
                    + "\n\nYou can also add GaGa in System Settings > General > Login Items."
            )
        }

        loginItem.state = service.status == .enabled ? .on : .off
    }

    //
    // Updates
    //

    @objc func onCheckUpdatesClicked() {
        checkUpdatesItem.state = checkUpdatesItem.state == .on ? .off : .on
    }

    @objc func onCheckNowClicked() {
        checkForUpdates(interactive: true)
    }

    /// Look for a newer release. Interactive: also report "up to date" and errors.
    func checkForUpdates(interactive: Bool) {
        guard !updateCheckRunning else { return }
        updateCheckRunning = true

        Updater.check { [weak self] result in
            guard let self else { return }
            self.updateCheckRunning = false

            switch result {
            case .failure(let error):
                if interactive {
                    Alert.show(title: "GaGa updates", message: "Unable to check for updates:\n" + error.localizedDescription)
                }

            case .success(nil):
                if interactive {
                    Alert.show(title: "GaGa updates", message: "You have the latest GaGa (\(Updater.currentVersion)).")
                }

            case .success(let update?):
                let confirmed = Alert.confirm(
                    title: "GaGa \(update.version) is available",
                    message: "You have \(Updater.currentVersion). Download it and restart GaGa now?",
                    confirmTitle: "Update"
                )

                if confirmed {
                    self.installUpdate(update)
                }
            }
        }
    }

    private func installUpdate(_ update: Updater.Update) {
        Updater.install(update) { result in
            switch result {
            case .success:
                // applicationWillTerminate saves the settings:
                Updater.relaunch()

            case .failure(let error):
                Alert.show(
                    title: "Unable to install the update",
                    message: error.localizedDescription
                        + (update.page.map { "\n\nYou can download it from:\n\($0.absoluteString)" } ?? "")
                )
            }
        }
    }
}
