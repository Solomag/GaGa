
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// A port of mINI, the minimal INI reader used by the Windows version.
// Subclasses override the handlers they care about.

import Foundation

class INIReader {

    /// Called when the current line is empty.
    func onEmpty() throws {}

    /// Called when the current line is a comment, prefix (; or #) included.
    func onComment(_ text: String) throws {}

    /// Called when the current line is a section, before reading subsections.
    /// The name is complete, regardless of subsections and inner whitespace.
    func onSection(_ section: String) throws {}

    /// Called when a section name is empty, not including subsections.
    /// Called before onSection().
    func onSectionEmpty() throws {}

    /// Called each time a subsection is found in a section line.
    /// For a line such as [a/b/c] this is called 3 times, with the
    /// paths "a", "a/b" and "a/b/c".
    func onSubSection(_ subsection: String, path: String) throws {}

    /// Called when a subsection name is empty. Called before onSubSection().
    func onSubSectionEmpty(path: String) throws {}

    /// Called when the current line is a key=value pair.
    func onKeyValue(_ key: String, _ value: String) throws {}

    /// Called when the key is empty in a key=value pair.
    /// Called before onKeyValue().
    func onKeyEmpty(_ value: String) throws {}

    /// Called when the value is empty in a key=value pair.
    /// Called before onKeyValue().
    func onValueEmpty(_ key: String) throws {}

    /// Called when the reader is unable to read the current line.
    /// The line is passed untrimmed.
    func onUnknown(_ line: String) throws {}

    /// Read a single INI line, dispatching to the handlers above.
    func readLine(_ line: String) throws {
        let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)

        if trimmed.isEmpty {
            try onEmpty()
            return
        }

        if trimmed.hasPrefix("#") || trimmed.hasPrefix(";") {
            try onComment(trimmed)
            return
        }

        if trimmed.hasPrefix("[") && trimmed.hasSuffix("]") {
            let section = String(trimmed.dropFirst().dropLast())
                .trimmingCharacters(in: .whitespaces)

            if section.isEmpty {
                try onSectionEmpty()
            }

            try onSection(section)
            try readSubSections(section)
            return
        }

        if let separator = trimmed.firstIndex(of: "=") {
            let key = String(trimmed[trimmed.startIndex ..< separator])
                .trimmingCharacters(in: .whitespaces)
            let value = String(trimmed[trimmed.index(after: separator)...])
                .trimmingCharacters(in: .whitespaces)

            if key.isEmpty {
                try onKeyEmpty(value)
            }

            if value.isEmpty {
                try onValueEmpty(key)
            }

            try onKeyValue(key, value)
            return
        }

        try onUnknown(line)
    }

    /// Read the subsections in a section name, accumulating the path.
    private func readSubSections(_ section: String) throws {
        let subsections = section.components(separatedBy: "/")

        // the first subsection is special, no separator, name/path identical:
        var path = subsections[0].trimmingCharacters(in: .whitespaces)

        if path.isEmpty {
            try onSubSectionEmpty(path: path)
        }

        try onSubSection(path, path: path)

        for subsection in subsections.dropFirst() {
            let name = subsection.trimmingCharacters(in: .whitespaces)
            path += "/" + name

            if name.isEmpty {
                try onSubSectionEmpty(path: path)
            }

            try onSubSection(name, path: path)
        }
    }
}
