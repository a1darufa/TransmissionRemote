import Foundation
import os
import Synchronization

/// The app's diagnostic messages, written to the unified log (Console.app, `log stream`) and,
/// once `startFileLogging(in:)` has been called, to daily files a user can attach to a bug report.
///
/// macOS keeps unified log entries only for a short while and a sandboxed app can't read back
/// earlier launches, so the files are what makes a problem from yesterday diagnosable.
/// Messages are stored in plain text: never pass passwords, credentials or request bodies.
public struct DiagnosticLog: Sendable {
    public enum Level: String, Sendable {
        case debug = "DBG"
        case info = "INF"
        case warning = "WRN"
        case error = "ERR"
    }

    private static let fileWriter = Mutex<LogFileWriter?>(nil)

    private let category: String
    private let logger: Logger

    public init(category: String, subsystem: String = "TransmissionRemote") {
        self.category = category
        self.logger = Logger(subsystem: subsystem, category: category)
    }

    /// Frequent details, such as every successful call. Unified log only, never the file.
    public func debug(_ message: String) {
        logger.debug("\(message, privacy: .public)")
    }

    public func info(_ message: String) {
        logger.info("\(message, privacy: .public)")
        Self.writeToFile(.info, category: category, message: message)
    }

    public func warning(_ message: String) {
        logger.warning("\(message, privacy: .public)")
        Self.writeToFile(.warning, category: category, message: message)
    }

    public func error(_ message: String) {
        logger.error("\(message, privacy: .public)")
        Self.writeToFile(.error, category: category, message: message)
    }

    /// Starts copying messages to daily files in `directory`, and deletes files older than
    /// `retentionDays`.
    public static func startFileLogging(in directory: URL, retentionDays: Int = 14) throws {
        // Created inside the lock, so the writer is never reachable from outside it.
        try fileWriter.withLock { writer in
            writer = try LogFileWriter(directory: directory, retentionDays: retentionDays)
        }
    }

    /// Where the log files are, once file logging has started.
    public static var logDirectory: URL? {
        fileWriter.withLock { $0?.directory }
    }

    private static func writeToFile(_ level: Level, category: String, message: String) {
        fileWriter.withLock { writer in
            writer?.write(level, category: category, message: message)
        }
    }
}

/// Appends lines to `TransmissionRemote-<date>.log`, one file per day.
///
/// Callers serialize access (`DiagnosticLog` holds it in a mutex). Logging is rare enough at
/// the levels written here (connections and failures) that writing synchronously is fine.
final class LogFileWriter {
    static let filePrefix = "TransmissionRemote-"
    /// A runaway loop shouldn't fill the disk; the unified log still gets everything.
    static let maximumBytesPerDay = 10 * 1024 * 1024

    let directory: URL
    private let maximumBytes: Int
    private var day = ""
    private var handle: FileHandle?
    private var bytesToday = 0

    init(
        directory: URL,
        retentionDays: Int,
        maximumBytes: Int = LogFileWriter.maximumBytesPerDay,
        now: Date = .now
    ) throws {
        self.directory = directory
        self.maximumBytes = maximumBytes
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        deleteFiles(olderThan: retentionDays, now: now)
    }

    deinit {
        try? handle?.close()
    }

    func write(_ level: DiagnosticLog.Level, category: String, message: String, at date: Date = .now) {
        let today = date.formatted(Self.dayFormat)
        if today != day {
            openFile(for: today)
        }
        guard let handle, bytesToday < maximumBytes else { return }

        // One message per line, so the file can be searched and filtered with grep.
        let flattened = message.replacingOccurrences(of: "\n", with: " ⏎ ")
        var line = "\(date.formatted(Self.timeFormat)) [\(level.rawValue)] \(category): \(flattened)\n"
        if bytesToday + line.utf8.count >= maximumBytes {
            line = "\(date.formatted(Self.timeFormat)) [WRN] Log: today's log is full; "
                + "further messages go only to the unified log.\n"
        }
        let data = Data(line.utf8)
        do {
            try handle.write(contentsOf: data)
            bytesToday += data.count
        } catch {
            // Nowhere sensible to report a failing log; stop trying until tomorrow.
            bytesToday = maximumBytes
        }
    }

    func fileURL(for day: String) -> URL {
        directory.appending(path: "\(Self.filePrefix)\(day).log")
    }

    private func openFile(for newDay: String) {
        try? handle?.close()
        handle = nil
        day = newDay
        let url = fileURL(for: newDay)
        if !FileManager.default.fileExists(atPath: url.path) {
            FileManager.default.createFile(atPath: url.path, contents: nil)
        }
        handle = try? FileHandle(forWritingTo: url)
        bytesToday = Int((try? handle?.seekToEnd()) ?? 0)
    }

    private func deleteFiles(olderThan retentionDays: Int, now: Date) {
        let cutoff = now.addingTimeInterval(-Double(retentionDays) * 24 * 60 * 60)
        let files = (try? FileManager.default.contentsOfDirectory(
            at: directory, includingPropertiesForKeys: [.contentModificationDateKey]
        )) ?? []
        for file in files where file.lastPathComponent.hasPrefix(Self.filePrefix) {
            let modified = try? file.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate
            if let modified, modified < cutoff {
                try? FileManager.default.removeItem(at: file)
            }
        }
    }

    private static let dayFormat = Date.ISO8601FormatStyle(timeZone: .current).year().month().day()
    private static let timeFormat = Date.ISO8601FormatStyle(includingFractionalSeconds: true, timeZone: .current)
}
