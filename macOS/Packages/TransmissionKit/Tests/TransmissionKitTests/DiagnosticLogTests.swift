import Foundation
import Testing
@testable import TransmissionKit

@Suite("Diagnostic log files")
struct DiagnosticLogTests {
    private let directory: URL

    init() throws {
        directory = FileManager.default.temporaryDirectory.appending(path: "DiagnosticLogTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    @Test func writesOneLinePerMessageWithLevelAndCategory() throws {
        let writer = try LogFileWriter(directory: directory, retentionDays: 14)
        let now = Date.now

        writer.write(.warning, category: "RPC", message: "torrent-get failed:\nHTTP 500", at: now)
        writer.write(.info, category: "Connection", message: "Connected", at: now)

        let lines = try contents(of: writer, on: now).split(separator: "\n")
        #expect(lines.count == 2, "a line break inside a message must not start a new entry")
        #expect(lines[0].contains("[WRN] RPC: torrent-get failed: ⏎ HTTP 500"))
        #expect(lines[1].contains("[INF] Connection: Connected"))
    }

    @Test func appendsToTodaysFileAcrossLaunches() throws {
        let now = Date.now
        try LogFileWriter(directory: directory, retentionDays: 14).write(.info, category: "App", message: "first", at: now)
        let second = try LogFileWriter(directory: directory, retentionDays: 14)
        second.write(.info, category: "App", message: "second", at: now)

        let text = try contents(of: second, on: now)
        #expect(text.contains("first") && text.contains("second"))
    }

    @Test func stopsAtTheDailyLimitWithOneNotice() throws {
        let writer = try LogFileWriter(directory: directory, retentionDays: 14, maximumBytes: 300)
        let now = Date.now

        for index in 0..<20 {
            writer.write(.error, category: "Loop", message: "message number \(index)", at: now)
        }

        let text = try contents(of: writer, on: now)
        #expect(text.utf8.count < 450)
        #expect(text.components(separatedBy: "today's log is full").count == 2, "the notice appears once")
    }

    @Test func deletesOnlyOldLogFiles() throws {
        let old = directory.appending(path: "TransmissionRemote-2020-01-01.log")
        let recent = directory.appending(path: "TransmissionRemote-2026-11-01.log")
        let unrelated = directory.appending(path: "notes.txt")
        for file in [old, recent, unrelated] {
            FileManager.default.createFile(atPath: file.path, contents: Data("x".utf8))
        }
        let now = Date.now
        try FileManager.default.setAttributes(
            [.modificationDate: now.addingTimeInterval(-30 * 24 * 60 * 60)], ofItemAtPath: old.path
        )
        try FileManager.default.setAttributes(
            [.modificationDate: now.addingTimeInterval(-30 * 24 * 60 * 60)], ofItemAtPath: unrelated.path
        )

        _ = try LogFileWriter(directory: directory, retentionDays: 14, now: now)

        #expect(!FileManager.default.fileExists(atPath: old.path))
        #expect(FileManager.default.fileExists(atPath: recent.path))
        #expect(FileManager.default.fileExists(atPath: unrelated.path), "only the app's own log files are pruned")
    }

    private func contents(of writer: LogFileWriter, on date: Date) throws -> String {
        let day = date.formatted(Date.ISO8601FormatStyle(timeZone: .current).year().month().day())
        return try String(contentsOf: writer.fileURL(for: day), encoding: .utf8)
    }
}
