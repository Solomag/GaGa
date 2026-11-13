// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// Checks the GitHub releases of Solomag/GaGa for a newer macOS build
// (an asset named like GaGa-macOS-<version>.zip), and replaces the running
// app bundle with it.

import AppKit

enum Updater {

    private static let latestRelease = URL(string: "https://api.github.com/repos/Solomag/GaGa/releases/latest")!

    struct Update {
        let version: String
        let page: URL?
        let download: URL
    }

    enum UpdateError: LocalizedError {
        case badArchive
        case notWritable(String)
        case rateLimited

        var errorDescription: String? {
            switch self {
            case .badArchive:
                return "The downloaded file doesn't contain GaGa.app."
            case .notWritable(let path):
                return "GaGa can't replace itself in:\n\(path)"
            case .rateLimited:
                return "GitHub refused the request (too many requests), try again later."
            }
        }
    }

    private struct Release: Decodable {
        let tag_name: String
        let html_url: String?
        let assets: [Asset]
    }

    private struct Asset: Decodable {
        let name: String
        let browser_download_url: String
    }

    /// Version of the running app (CFBundleShortVersionString).
    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    /// "v2026.11.12" -> [2026, 11, 12]
    static func parts(_ version: String) -> [Int] {
        version
            .trimmingCharacters(in: CharacterSet(charactersIn: "vV "))
            .split(separator: ".")
            .map { Int($0) ?? 0 }
    }

    /// Whether a version is newer than another, missing parts being 0.
    static func isNewer(_ candidate: String, than current: String) -> Bool {
        let a = parts(candidate)
        let b = parts(current)

        for index in 0..<max(a.count, b.count) {
            let x = index < a.count ? a[index] : 0
            let y = index < b.count ? b[index] : 0
            if x != y { return x > y }
        }

        return false
    }

    /// The latest release, when it is newer than us and has a macOS build.
    /// The completion runs on the main queue.
    static func check(completion: @escaping (Result<Update?, Error>) -> Void) {
        checkWithAPI { result in
            // the API allows 60 requests an hour per address:
            if case .failure(UpdateError.rateLimited) = result {
                checkWithRedirect(completion: completion)
            } else {
                completion(result)
            }
        }
    }

    /// Without the API: .../releases/latest redirects to .../releases/tag/TAG,
    /// and the assets of a tag have fixed addresses.
    private static func checkWithRedirect(completion: @escaping (Result<Update?, Error>) -> Void) {
        let latest = URL(string: "https://github.com/Solomag/GaGa/releases/latest")!

        URLSession.shared.dataTask(with: latest) { _, response, error in
            let result: Result<Update?, Error> = Result {
                if let error { throw error }

                guard let url = response?.url, url.path.contains("/tag/") else { return nil }
                let tag = url.lastPathComponent

                guard isNewer(tag, than: currentVersion),
                      let download = URL(string: "https://github.com/Solomag/GaGa/releases/download/\(tag)/GaGa-macOS-\(tag).zip")
                else { return nil }

                return Update(version: tag, page: url, download: download)
            }

            DispatchQueue.main.async { completion(result) }
        }.resume()
    }

    private static func checkWithAPI(completion: @escaping (Result<Update?, Error>) -> Void) {
        var request = URLRequest(url: latestRelease, timeoutInterval: 15)
        request.setValue("GaGa updater", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")

        URLSession.shared.dataTask(with: request) { data, response, error in
            let result: Result<Update?, Error> = Result {
                if let error { throw error }

                if let http = response as? HTTPURLResponse, http.statusCode == 403 || http.statusCode == 429 {
                    throw UpdateError.rateLimited
                }

                let release = try JSONDecoder().decode(Release.self, from: data ?? Data())

                guard isNewer(release.tag_name, than: currentVersion) else { return nil }

                let asset = release.assets.first {
                    $0.name.contains("macOS") && $0.name.hasSuffix(".zip")
                }

                guard let asset, let download = URL(string: asset.browser_download_url) else { return nil }

                return Update(
                    version: release.tag_name,
                    page: release.html_url.flatMap(URL.init(string:)),
                    download: download
                )
            }

            DispatchQueue.main.async { completion(result) }
        }.resume()
    }

    /// Download the update and put it in place of the running app.
    /// The completion runs on the main queue.
    static func install(_ update: Update, completion: @escaping (Result<Void, Error>) -> Void) {
        var request = URLRequest(url: update.download, timeoutInterval: 60)
        request.setValue("GaGa updater", forHTTPHeaderField: "User-Agent")

        URLSession.shared.downloadTask(with: request) { location, _, error in
            let result = Result<Void, Error> {
                if let error { throw error }
                guard let location else { throw UpdateError.badArchive }
                try replaceApp(withZip: location)
            }

            DispatchQueue.main.async { completion(result) }
        }.resume()
    }

    private static func replaceApp(withZip zip: URL) throws {
        let manager = FileManager.default
        let current = Bundle.main.bundleURL
        let parent = current.deletingLastPathComponent()

        guard manager.isWritableFile(atPath: parent.path) else {
            throw UpdateError.notWritable(parent.path)
        }

        let work = manager.temporaryDirectory.appendingPathComponent("GaGa-update-\(UUID().uuidString)")
        try manager.createDirectory(at: work, withIntermediateDirectories: true)
        defer { try? manager.removeItem(at: work) }

        // ditto keeps the bundle intact (symlinks, permissions, signature):
        let ditto = Process()
        ditto.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        ditto.arguments = ["-x", "-k", zip.path, work.path]
        try ditto.run()
        ditto.waitUntilExit()

        let app = work.appendingPathComponent("GaGa.app")
        guard ditto.terminationStatus == 0,
              manager.fileExists(atPath: app.appendingPathComponent("Contents/MacOS/GaGa").path)
        else {
            throw UpdateError.badArchive
        }

        // swap: the running app keeps working from memory until it quits
        _ = try manager.replaceItemAt(current, withItemAt: app)
    }

    /// Start the (new) app and quit this one.
    static func relaunch() {
        let path = Bundle.main.bundleURL.path

        // wait for this process to exit, then open the app again:
        let shell = Process()
        shell.executableURL = URL(fileURLWithPath: "/bin/sh")
        shell.arguments = ["-c", "while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.2; done; open \"$0\"", path]
        try? shell.run()

        NSApp.terminate(nil)
    }
}
