
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import Foundation

/// A track announced by a station.
struct HistoryEntry: Codable {
    let time: Date
    let station: String
    let title: String
}

/// Program settings, stored as JSON next to the streams file.
struct Settings: Codable {

    /// Last volume set in the audio menu, between 0 and 1.
    var volume: Double = 0.3

    /// Last stream played. Defaults to the local playlist, so that a
    /// fresh install plays it without picking anything first.
    var lastStream: PlayerStream? = PlayerStream(
        name: "Capris",
        url: AppPaths.defaultStream
    )

    /// Whether to start playing the last stream on startup.
    var autoPlay: Bool = false

    /// Whether the media keys are enabled.
    var mediaKeys: Bool = true

    /// Whether to look for a newer GaGa on startup.
    var checkUpdates: Bool = true

    /// Recently played stations, newest first.
    var recent: [PlayerStream] = []

    /// Track history, newest first.
    var history: [HistoryEntry] = []

    /// Move AAC recordings into .m4a (same audio, more players open it).
    var aacAsM4a: Bool = true

    init() {}

    /// Decode leniently, so that a file written by an older version
    /// or missing keys still loads with defaults for the rest.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = Settings()

        volume = try container.decodeIfPresent(Double.self, forKey: .volume) ?? defaults.volume
        lastStream = try container.decodeIfPresent(PlayerStream.self, forKey: .lastStream)
            ?? defaults.lastStream
        autoPlay = try container.decodeIfPresent(Bool.self, forKey: .autoPlay) ?? defaults.autoPlay
        mediaKeys = try container.decodeIfPresent(Bool.self, forKey: .mediaKeys) ?? defaults.mediaKeys
        checkUpdates = try container.decodeIfPresent(Bool.self, forKey: .checkUpdates)
            ?? defaults.checkUpdates
        recent = (try? container.decodeIfPresent([PlayerStream].self, forKey: .recent)) ?? []
        history = (try? container.decodeIfPresent([HistoryEntry].self, forKey: .history)) ?? []
        aacAsM4a = (try? container.decodeIfPresent(Bool.self, forKey: .aacAsM4a)) ?? defaults.aacAsM4a

        volume = min(max(volume, 0), 1)
    }

    /// Load settings from a file, returning defaults when it doesn't exist.
    /// Throws when the file exists but can't be read or parsed.
    static func load(from url: URL) throws -> Settings {
        guard FileManager.default.fileExists(atPath: url.path) else {
            return Settings()
        }

        return try JSONDecoder().decode(Settings.self, from: Data(contentsOf: url))
    }

    /// Save the current settings.
    func save(to url: URL) throws {
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(self).write(to: url, options: .atomic)
    }
}
