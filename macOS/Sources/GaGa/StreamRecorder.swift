// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

// AVPlayer doesn't hand out the bytes it plays, so a recording is a second
// connection to the same stream, saved as received: no re-encoding, and no
// ICY metadata (it is only sent to clients that ask for it).

import AVFoundation
import Foundation

final class StreamRecorder: NSObject, URLSessionDataDelegate {

    private var session: URLSession?
    private var task: URLSessionDataTask?
    private var file: FileHandle?
    private var basePath: URL?

    /// File being written, once the stream answered.
    private(set) var fileURL: URL?

    /// Whether a recording is running.
    var isRecording: Bool { task != nil }

    /// Move AAC recordings into .m4a when they are closed.
    static var aacAsM4a = true

    /// Called on the main queue when a recording is finished and in its
    /// final place (after the .m4a move, if any).
    var onSaved: ((URL) -> Void)?

    /// Called on the main queue when a recording stops on its own (an error,
    /// or the stream ending), with the reason.
    var onFailure: ((String) -> Void)?

    /// File extension for a stream content type.
    static func fileExtension(for mimeType: String?) -> String {
        let type = (mimeType ?? "").lowercased()

        if type.contains("flac") { return "flac" }
        if type.contains("aac") || type.contains("mp4") { return "aac" }
        if type.contains("ogg") { return "ogg" }
        if type.contains("opus") { return "opus" }
        if type.contains("wav") { return "wav" }
        return "mp3"
    }

    /// Start recording a stream into basePath plus an extension that
    /// matches the stream format.
    func start(_ stream: URL, basePath: URL) {
        stop()

        self.basePath = basePath

        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = 30
        configuration.timeoutIntervalForResource = .infinity

        let session = URLSession(configuration: configuration, delegate: self, delegateQueue: nil)

        var request = URLRequest(url: stream)
        request.setValue("GaGa/1.0 (macOS; radio player)", forHTTPHeaderField: "User-Agent")

        let task = session.dataTask(with: request)
        self.session = session
        self.task = task
        task.resume()
    }

    /// Stop recording, keeping what was recorded.
    func stop() {
        task?.cancel()
        task = nil
        session?.invalidateAndCancel()
        session = nil
        closeFile()
    }

    private func closeFile() {
        let wasOpen = file != nil
        try? file?.close()
        file = nil

        guard wasOpen, let url = fileURL else { return }

        if Self.aacAsM4a && url.pathExtension.lowercased() == "aac" {
            moveToM4a(url)
        } else {
            onSaved?(url)
        }
    }

    /// Rewrite a finished .aac recording as .m4a, copying the audio as is.
    /// On any problem the .aac file stays.
    private func moveToM4a(_ aac: URL) {
        let base = aac.deletingPathExtension()
        var m4a = base.appendingPathExtension("m4a")
        var number = 2
        while FileManager.default.fileExists(atPath: m4a.path) {
            m4a = URL(fileURLWithPath: base.path + " (\(number))").appendingPathExtension("m4a")
            number += 1
        }

        let asset = AVURLAsset(url: aac)
        guard let export = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetPassthrough) else {
            onSaved?(aac)
            return
        }

        export.outputURL = m4a
        export.outputFileType = .m4a

        export.exportAsynchronously { [weak self] in
            DispatchQueue.main.async {
                guard let self else { return }

                if export.status == .completed {
                    try? FileManager.default.removeItem(at: aac)
                    if self.fileURL == aac { self.fileURL = m4a }
                    self.onSaved?(m4a)
                } else {
                    try? FileManager.default.removeItem(at: m4a)
                    self.onSaved?(aac)
                }
            }
        }
    }

    //
    // URLSessionDataDelegate (on the session queue)
    //

    func urlSession(
        _ session: URLSession,
        dataTask: URLSessionDataTask,
        didReceive response: URLResponse,
        completionHandler: @escaping (URLSession.ResponseDisposition) -> Void
    ) {
        if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
            completionHandler(.cancel)
            report("The server answered \(http.statusCode).")
            return
        }

        guard let basePath else {
            completionHandler(.cancel)
            return
        }

        let ext = Self.fileExtension(for: response.mimeType)

        do {
            try FileManager.default.createDirectory(
                at: basePath.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )

            var url = basePath.appendingPathExtension(ext)
            var number = 2
            while FileManager.default.fileExists(atPath: url.path) {
                url = URL(fileURLWithPath: basePath.path + " (\(number))").appendingPathExtension(ext)
                number += 1
            }

            FileManager.default.createFile(atPath: url.path, contents: nil)
            file = try FileHandle(forWritingTo: url)

            DispatchQueue.main.async { self.fileURL = url }
            completionHandler(.allow)
        } catch {
            completionHandler(.cancel)
            report(error.localizedDescription)
        }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        do {
            try file?.write(contentsOf: data)
        } catch {
            dataTask.cancel()
            report(error.localizedDescription)
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        // cancelled by stop(): nothing to report
        if let error = error as? URLError, error.code == .cancelled {
            return
        }

        report(error?.localizedDescription ?? "The stream ended.")
    }

    private func report(_ message: String) {
        DispatchQueue.main.async {
            guard self.task != nil else { return }
            self.stop()
            self.onFailure?(message)
        }
    }
}
