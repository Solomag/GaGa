// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// Recent stations, track history and the sleep timer.

import AppKit

extension AppController {

    private static let recentLimit = 15
    private static let historyLimit = 100
    private static let historyShown = 30
    private static let sleepPresets = [15, 30, 60, 120]

    /// Rebuild the submenus, called when the menu is about to open.
    func updateExtrasMenus() {
        recentItem.submenu = recentMenu()
        historyItem.submenu = historyMenu()
        sleepItem.submenu = sleepMenu()

        if let sleepAt {
            let minutes = max(0, Int((sleepAt.timeIntervalSinceNow / 60).rounded(.up)))
            sleepItem.title = "Sleep Timer (\(minutes) min left)"
            sleepItem.state = .on
        } else {
            sleepItem.title = "Sleep Timer"
            sleepItem.state = .off
        }
    }

    private func plainMenu(_ title: String) -> NSMenu {
        let menu = NSMenu(title: title)
        menu.autoenablesItems = false
        return menu
    }

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    private func actionItem(_ title: String, _ action: Selector, _ object: Any? = nil) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.representedObject = object
        return item
    }

    private static func shortened(_ text: String) -> String {
        text.count > 70 ? String(text.prefix(67)) + "..." : text
    }

    //
    // Recent stations
    //

    func addRecent(_ stream: PlayerStream) {
        recent.removeAll { $0.url == stream.url }
        recent.insert(stream, at: 0)

        if recent.count > Self.recentLimit {
            recent.removeLast(recent.count - Self.recentLimit)
        }
    }

    private func recentMenu() -> NSMenu {
        let menu = plainMenu("Recent Stations")

        guard !recent.isEmpty else {
            menu.addItem(disabledItem("No Stations Played Yet"))
            return menu
        }

        for stream in recent {
            let item = actionItem(Self.shortened(stream.name), #selector(onRecentClicked(_:)), stream)
            item.state = player.source?.url == stream.url ? .on : .off
            menu.addItem(item)
        }

        menu.addItem(.separator())
        menu.addItem(actionItem("Clear Recent Stations", #selector(onClearRecentClicked)))
        return menu
    }

    @objc private func onRecentClicked(_ sender: NSMenuItem) {
        guard let stream = sender.representedObject as? PlayerStream else { return }
        player.play(stream)
    }

    @objc private func onClearRecentClicked() {
        recent.removeAll()
    }

    //
    // Track history
    //

    /// Whether a stream title is an actual track and not the station
    /// talking about itself ("Station name", "Live", slogans, addresses).
    static func isTrackTitle(_ title: String, station: String) -> Bool {
        let text = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.count >= 3 else { return false }

        let lower = text.lowercased()
        let junk: Set<String> = ["live", "on air", "buffering", "unknown", "advertisement", "commercial", "-", "n/a", "null"]
        if junk.contains(lower) { return false }

        let phrases = ["commercial-free", "commercial free", "listener-supported", "listener supported",
                       "advertis", "www.", "http://", "https://", "station id", "jingle"]
        if phrases.contains(where: { lower.contains($0) }) { return false }

        return text.caseInsensitiveCompare(station.trimmingCharacters(in: .whitespaces)) != .orderedSame
    }

    func addHistory(_ title: String) {
        let station = player.source?.name ?? ""
        guard Self.isTrackTitle(title, station: station) else { return }

        let text = title.trimmingCharacters(in: .whitespacesAndNewlines)
        if let last = history.first, last.title == text, last.station == station { return }

        history.insert(HistoryEntry(time: Date(), station: station, title: text), at: 0)

        if history.count > Self.historyLimit {
            history.removeLast(history.count - Self.historyLimit)
        }
    }

    private func historyMenu() -> NSMenu {
        let menu = plainMenu("Track History")

        guard !history.isEmpty else {
            menu.addItem(disabledItem("No Tracks Yet"))
            return menu
        }

        let time = DateFormatter()
        time.dateFormat = "HH:mm"

        for entry in history.prefix(Self.historyShown) {
            let item = NSMenuItem(
                title: time.string(from: entry.time) + "   " + Self.shortened(entry.title),
                action: nil,
                keyEquivalent: ""
            )
            item.toolTip = entry.station

            let submenu = plainMenu(entry.title)
            submenu.addItem(actionItem("Copy", #selector(onCopyTrackClicked(_:)), entry.title))
            submenu.addItem(actionItem("Search on the Web", #selector(onSearchTrackClicked(_:)), entry.title))
            item.submenu = submenu

            menu.addItem(item)
        }

        menu.addItem(.separator())
        menu.addItem(actionItem("Copy the Whole History", #selector(onCopyHistoryClicked)))
        menu.addItem(actionItem("Clear History", #selector(onClearHistoryClicked)))
        return menu
    }

    private func copy(_ text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    @objc private func onCopyTrackClicked(_ sender: NSMenuItem) {
        guard let title = sender.representedObject as? String else { return }
        copy(title)
    }

    @objc private func onSearchTrackClicked(_ sender: NSMenuItem) {
        guard let title = sender.representedObject as? String,
              let query = title.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
              let url = URL(string: "https://duckduckgo.com/?q=" + query)
        else { return }

        NSWorkspace.shared.open(url)
    }

    @objc private func onCopyHistoryClicked() {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm"

        copy(history
            .map { "\(formatter.string(from: $0.time))  \($0.title)  (\($0.station))" }
            .joined(separator: "\n"))
    }

    @objc private func onClearHistoryClicked() {
        history.removeAll()
    }

    //
    // Sleep timer
    //

    private func sleepMenu() -> NSMenu {
        let menu = plainMenu("Sleep Timer")

        for minutes in Self.sleepPresets {
            let title = minutes < 60 ? "\(minutes) Minutes" : (minutes == 60 ? "1 Hour" : "\(minutes / 60) Hours")
            let item = actionItem(title, #selector(onSleepClicked(_:)), minutes)
            menu.addItem(item)
        }

        menu.addItem(.separator())

        let off = actionItem("Off", #selector(onSleepOffClicked))
        off.state = sleepAt == nil ? .on : .off
        menu.addItem(off)
        return menu
    }

    @objc private func onSleepClicked(_ sender: NSMenuItem) {
        guard let minutes = sender.representedObject as? Int else { return }

        sleepTimer?.invalidate()
        sleepAt = Date().addingTimeInterval(TimeInterval(minutes * 60))

        let timer = Timer(timeInterval: TimeInterval(minutes * 60), repeats: false) { [weak self] _ in
            self?.player.stop()
            self?.sleepTimer = nil
            self?.sleepAt = nil
        }
        RunLoop.main.add(timer, forMode: .common)
        sleepTimer = timer
    }

    @objc private func onSleepOffClicked() {
        sleepTimer?.invalidate()
        sleepTimer = nil
        sleepAt = nil
    }
}
