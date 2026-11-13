// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import Foundation

/// Adds streams to the streams file, keeping everything else
/// (comments, order, formatting) exactly as the user wrote it.
/// Same behavior as StreamsFileWriter in the Windows version.
enum StreamsFileWriter {

    /// A station name made safe as an INI key: no '=' (it splits key and
    /// value), no leading characters that would make it a comment or a section.
    static func cleanName(_ name: String) -> String {
        var clean = name
            .replacingOccurrences(of: "=", with: "-")
            .replacingOccurrences(of: "\r", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\t", with: " ")
            .trimmingCharacters(in: .whitespaces)

        while let first = clean.first, "#;[".contains(first) {
            clean.removeFirst()
            clean = clean.trimmingCharacters(in: .whitespaces)
        }

        return clean.isEmpty ? "Station" : clean
    }

    private static func readLines(_ url: URL) -> [String] {
        guard let data = try? Data(contentsOf: url) else { return [] }

        return String(textData: data)
            .replacingOccurrences(of: "\r\n", with: "\n")
            .components(separatedBy: "\n")
    }

    private static func isSection(_ line: String) -> Bool {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        return trimmed.hasPrefix("[") && trimmed.hasSuffix("]")
    }

    /// Whether the streams file already has a stream with this address.
    static func contains(_ fileURL: URL, stream: URL) -> Bool {
        for raw in readLines(fileURL) {
            let line = raw.trimmingCharacters(in: .whitespaces)

            if line.hasPrefix("#") || line.hasPrefix(";") || line.hasPrefix("[") {
                continue
            }

            guard let equals = line.firstIndex(of: "=") else { continue }

            let value = line[line.index(after: equals)...].trimmingCharacters(in: .whitespaces)
            if URL(string: value) == stream {
                return true
            }
        }

        return false
    }

    /// The address of a "name = url" line, or nil.
    private static func lineURL(_ raw: String) -> URL? {
        let line = raw.trimmingCharacters(in: .whitespaces)

        if line.hasPrefix("#") || line.hasPrefix(";") || line.hasPrefix("[") {
            return nil
        }

        guard let equals = line.firstIndex(of: "=") else { return nil }
        return URL(string: line[line.index(after: equals)...].trimmingCharacters(in: .whitespaces))
    }

    private static func isHeader(_ line: String, _ section: String) -> Bool {
        line.trimmingCharacters(in: .whitespaces)
            .caseInsensitiveCompare("[" + section + "]") == .orderedSame
    }

    /// Whether a given section (not its subsections) has a stream with this address.
    static func containsInSection(_ fileURL: URL, section: String, stream: URL) -> Bool {
        var inside = false

        for line in readLines(fileURL) {
            if isSection(line) {
                inside = isHeader(line, section)
            } else if inside, lineURL(line) == stream {
                return true
            }
        }

        return false
    }

    /// Remove the streams with this address from a section.
    /// A section left without lines is removed too.
    static func removeStream(_ fileURL: URL, section: String, stream: URL) throws {
        var lines = readLines(fileURL)

        guard let start = lines.firstIndex(where: { isHeader($0, section) }) else { return }

        var end = start + 1
        while end < lines.count, !isSection(lines[end]) {
            end += 1
        }

        var index = end - 1
        while index > start {
            if lineURL(lines[index]) == stream {
                lines.remove(at: index)
                end -= 1
            }
            index -= 1
        }

        let empty = lines[(start + 1)..<end].allSatisfy {
            $0.trimmingCharacters(in: .whitespaces).isEmpty
        }

        if empty {
            lines.removeSubrange(start..<end)
        }

        while let last = lines.last, last.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.removeLast()
        }

        let text = lines.joined(separator: "\n") + "\n"
        try text.write(to: fileURL, atomically: true, encoding: .utf8)
    }

    /// Add "name = url" at the end of a section. A missing section is
    /// created before the first section (newSectionFirst) or at the end.
    static func addStream(
        _ fileURL: URL,
        section: String,
        name: String,
        stream: URL,
        newSectionFirst: Bool = false
    ) throws {
        var lines = readLines(fileURL)

        // drop the empty entries produced by trailing newlines:
        while let last = lines.last, last.trimmingCharacters(in: .whitespaces).isEmpty {
            lines.removeLast()
        }

        let entry = cleanName(name) + " = " + stream.absoluteString
        let header = "[" + section + "]"

        let sectionLine = lines.firstIndex {
            $0.trimmingCharacters(in: .whitespaces).caseInsensitiveCompare(header) == .orderedSame
        }

        if let sectionLine {
            // after the last non-empty line of the section:
            var insertAt = sectionLine + 1
            var index = sectionLine + 1

            while index < lines.count, !isSection(lines[index]) {
                if !lines[index].trimmingCharacters(in: .whitespaces).isEmpty {
                    insertAt = index + 1
                }
                index += 1
            }

            lines.insert(entry, at: insertAt)
        } else if newSectionFirst, let firstSection = lines.firstIndex(where: isSection) {
            // keep any comments on top of the file above the new section:
            lines.insert(contentsOf: [header, entry, ""], at: firstSection)
        } else {
            if !lines.isEmpty {
                lines.append("")
            }
            lines.append(header)
            lines.append(entry)
        }

        let text = lines.joined(separator: "\n") + "\n"
        try text.write(to: fileURL, atomically: true, encoding: .utf8)
    }
}
