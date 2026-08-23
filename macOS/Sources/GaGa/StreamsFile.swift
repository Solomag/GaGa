
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// Reading the streams file into a tree of groups and streams, which both
// the menu bar menu and the main window are built from.

import AppKit

/// A node in the streams tree: either a named group of nodes, or a stream.
final class StreamNode {
    let name: String
    let stream: PlayerStream?
    private(set) var children: [StreamNode] = []

    init(group name: String) {
        self.name = name
        self.stream = nil
    }

    init(stream: PlayerStream) {
        self.name = stream.name
        self.stream = stream
    }

    var isGroup: Bool { stream == nil }

    func append(_ node: StreamNode) {
        children.append(node)
    }
}

/// A syntax error in the streams file, with the exact offending line.
struct StreamsFileReadError: LocalizedError {
    let message: String
    let filePath: String
    let line: String
    let lineNumber: Int

    var errorDescription: String? { message }
}

/// An INIReader that reads a streams file into a tree, with sections as
/// groups and key=value pairs as streams.
final class StreamsFileReader: INIReader {

    private var filePath: String = ""

    private var root = StreamNode(group: "")
    private var pending: [StreamNode] = []
    private var currentGroup: StreamNode
    private var seenGroups: [String: StreamNode] = [:]

    private var currentLineNumber: Int = 0
    private var currentLine: String = ""

    override init() {
        currentGroup = root
        super.init()
    }

    /// Read a streams file into a tree of groups and streams.
    func read(filePath: String) throws -> StreamNode {
        self.filePath = filePath

        root = StreamNode(group: "")
        pending.removeAll()
        currentGroup = root
        seenGroups.removeAll()
        currentLineNumber = 0
        currentLine = ""

        defer { resetState() }

        for line in try Self.readText(filePath: filePath).components(separatedBy: .newlines) {
            currentLineNumber += 1
            currentLine = line
            try readLine(line)
        }

        // add pending items for the last group:
        addPending()
        return root
    }

    /// Read the file as text, accepting UTF-8 with or without a BOM
    /// and falling back to Latin-1 so that a bad encoding is not fatal.
    private static func readText(filePath: String) throws -> String {
        let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
        let text = String(data: data, encoding: .utf8)
            ?? String(data: data, encoding: .isoLatin1)
            ?? ""

        return text.hasPrefix("\u{FEFF}") ? String(text.dropFirst()) : text
    }

    private func resetState() {
        filePath = ""
        pending.removeAll()
        seenGroups.removeAll()
        currentLineNumber = 0
        currentLine = ""
    }

    /// Add the collected items to the current group.
    private func addPending() {
        for item in pending {
            currentGroup.append(item)
        }
        pending.removeAll()
    }

    private func readError(_ message: String) -> StreamsFileReadError {
        StreamsFileReadError(
            message: message,
            filePath: filePath,
            line: currentLine,
            lineNumber: currentLineNumber
        )
    }

    // Do not accept groups or streams with no name, streams with no URL,
    // or lines we can't understand:

    override func onSectionEmpty() throws {
        throw readError("Empty menu name.")
    }

    override func onSubSectionEmpty(path: String) throws {
        throw readError("Empty submenu name.")
    }

    override func onKeyEmpty(_ value: String) throws {
        throw readError("Empty stream name.")
    }

    override func onValueEmpty(_ key: String) throws {
        throw readError("Empty stream URL.")
    }

    override func onUnknown(_ line: String) throws {
        throw readError("Invalid syntax.")
    }

    /// On an empty line, add collected items and go back to the root.
    override func onEmpty() throws {
        addPending()
        currentGroup = root
    }

    /// On a new section, add collected items and go back to the root.
    override func onSection(_ section: String) throws {
        addPending()
        currentGroup = root
    }

    /// On a subsection, add collected items, create a group
    /// and descend into it.
    override func onSubSection(_ subsection: String, path: String) throws {
        var group = seenGroups[path]

        // not seen, create and add to the current group
        // otherwise it's a duplicate and has already been added:
        if group == nil {
            let created = StreamNode(group: subsection)
            seenGroups[path] = created
            pending.append(created)
            group = created
        }

        addPending()
        currentGroup = group!
    }

    /// Add key=value pairs as streams.
    override func onKeyValue(_ key: String, _ value: String) throws {
        guard let url = URL(string: value), url.scheme != nil else {
            throw readError("Invalid stream URL: \(value)")
        }

        pending.append(StreamNode(stream: PlayerStream(name: key, url: url)))
    }
}

/// Can read or recreate a streams file.
final class StreamsFileLoader {

    let filePath: String

    private let reader = StreamsFileReader()

    init(filePath: String) {
        self.filePath = filePath
    }

    /// Read the streams file, recreating it first when it doesn't exist.
    func load() throws -> StreamNode {
        // make a single attempt to recreate the file when needed:
        if !FileManager.default.fileExists(atPath: filePath) {
            try FileManager.default.createDirectory(
                at: URL(fileURLWithPath: filePath).deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            try Self.defaultStreams.write(toFile: filePath, atomically: true, encoding: .utf8)
        }

        return try reader.read(filePath: filePath)
    }

    /// Written on first run, or when the streams file is deleted.
    /// The first entry is the local playlist, which is the default stream.
    static var defaultStreams: String {
        """
        ; GaGa streams file.
        ;
        ; Sections are menus, key = value pairs are streams.
        ; Use a / in a section name to create submenus, e.g. [Rock/Classic].
        ; Streams can be direct URLs or .m3u, .pls, .asx and .xspf playlists,
        ; local ones included. This file is reloaded when it changes.

        [Radio]
        Capris = \(AppPaths.defaultStream.absoluteString)

        [SomaFM]
        Groove Salad = https://ice1.somafm.com/groovesalad-128-mp3
        Drone Zone = https://ice1.somafm.com/dronezone-128-mp3
        Deep Space One = https://ice1.somafm.com/deepspaceone-128-mp3
        Indie Pop Rocks = https://ice1.somafm.com/indiepop-128-mp3
        Underground 80s = https://ice1.somafm.com/u80s-128-mp3
        Lush = https://ice1.somafm.com/lush-128-mp3

        [Radio Paradise]
        Main Mix = https://stream.radioparadise.com/mp3-192
        Mellow Mix = https://stream.radioparadise.com/mellow-192
        Rock Mix = https://stream.radioparadise.com/rock-192
        Global Mix = https://stream.radioparadise.com/global-192

        """
    }
}

extension NSMenu {

    /// Add the children of a group node as items and submenus.
    func addStreams(_ group: StreamNode, target: AnyObject, action: Selector) {
        for node in group.children {
            let item = NSMenuItem(title: node.name, action: nil, keyEquivalent: "")

            if node.isGroup {
                let submenu = NSMenu(title: node.name)
                submenu.autoenablesItems = false
                submenu.addStreams(node, target: target, action: action)
                item.submenu = submenu
            } else {
                item.action = action
                item.target = target
                item.representedObject = node.stream
            }

            addItem(item)
        }
    }
}
