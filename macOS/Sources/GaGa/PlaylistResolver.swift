
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// AVPlayer plays audio streams directly but doesn't understand playlist
// files, which is how many stations publish their streams. Resolve those
// to the first stream URL they point at before handing them to the player.

import Foundation

enum PlaylistResolver {

    /// Playlist formats we unwrap. .m3u8 is deliberately absent: it is HLS,
    /// which AVPlayer handles natively.
    private static let playlistExtensions: Set<String> = ["m3u", "pls", "asx", "xspf"]

    private static let maximumDepth = 3

    enum ResolveError: LocalizedError {
        case tooManyRedirects
        case empty(URL)

        var errorDescription: String? {
            switch self {
            case .tooManyRedirects:
                return "The playlist points to another playlist too many times."
            case .empty(let url):
                return "No stream found in the playlist:\n\(url.absoluteString)"
            }
        }
    }

    /// Resolve a URL to something playable, downloading and unwrapping
    /// playlist files as needed. The completion runs on the main queue.
    static func resolve(
        _ url: URL,
        depth: Int = 0,
        completion: @escaping (Result<URL, Error>) -> Void
    ) {
        let ext = url.pathExtension.lowercased()

        guard playlistExtensions.contains(ext) else {
            DispatchQueue.main.async { completion(.success(url)) }
            return
        }

        guard depth < maximumDepth else {
            DispatchQueue.main.async { completion(.failure(ResolveError.tooManyRedirects)) }
            return
        }

        if url.isFileURL {
            do {
                let text = try readText(Data(contentsOf: url))
                guard let next = firstURL(in: text, base: url) else {
                    DispatchQueue.main.async { completion(.failure(ResolveError.empty(url))) }
                    return
                }
                resolve(next, depth: depth + 1, completion: completion)
            } catch {
                DispatchQueue.main.async { completion(.failure(error)) }
            }
            return
        }

        let task = URLSession.shared.dataTask(with: url) { data, response, error in
            if let error {
                DispatchQueue.main.async { completion(.failure(error)) }
                return
            }

            let base = response?.url ?? url

            guard
                let data,
                let next = firstURL(in: readText(data), base: base)
            else {
                DispatchQueue.main.async { completion(.failure(ResolveError.empty(url))) }
                return
            }

            resolve(next, depth: depth + 1, completion: completion)
        }

        task.resume()
    }

    private static func readText(_ data: Data) -> String {
        String(data: data, encoding: .utf8) ?? String(data: data, encoding: .isoLatin1) ?? ""
    }

    /// Extract the first stream URL from playlist text. The format is
    /// detected from the content, so a mislabeled file still works.
    static func firstURL(in text: String, base: URL) -> URL? {
        let lowercased = text.lowercased()

        let candidate: String?

        if lowercased.contains("[playlist]") {
            candidate = firstPLSEntry(in: text)
        } else if lowercased.contains("<asx") || lowercased.contains("<ref ") {
            candidate = firstHREFEntry(in: text)
        } else if lowercased.contains("<playlist") || lowercased.contains("<location>") {
            candidate = firstXMLElement("location", in: text)
        } else {
            candidate = firstM3UEntry(in: text)
        }

        guard let candidate, !candidate.isEmpty else { return nil }
        return URL(string: candidate, relativeTo: base)?.absoluteURL
    }

    /// .pls: FileN = URL
    private static func firstPLSEntry(in text: String) -> String? {
        for line in text.components(separatedBy: .newlines) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            guard trimmed.lowercased().hasPrefix("file"),
                  let separator = trimmed.firstIndex(of: "=")
            else { continue }

            return String(trimmed[trimmed.index(after: separator)...])
                .trimmingCharacters(in: .whitespaces)
        }
        return nil
    }

    /// .asx: <ref href="URL" />
    private static func firstHREFEntry(in text: String) -> String? {
        firstMatch(of: "href\\s*=\\s*[\"']([^\"']+)[\"']", in: text)
    }

    /// .xspf: <location>URL</location>
    private static func firstXMLElement(_ name: String, in text: String) -> String? {
        firstMatch(of: "<\(name)>\\s*([^<]+?)\\s*</\(name)>", in: text)
    }

    /// .m3u: the first line that is not blank and not a comment.
    private static func firstM3UEntry(in text: String) -> String? {
        text.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .first { !$0.isEmpty && !$0.hasPrefix("#") }
    }

    private static func firstMatch(of pattern: String, in text: String) -> String? {
        guard
            let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
            let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
            let range = Range(match.range(at: 1), in: text)
        else { return nil }

        return String(text[range])
    }
}
