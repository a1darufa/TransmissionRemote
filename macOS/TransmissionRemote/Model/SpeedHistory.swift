import Foundation

struct SpeedSample: Identifiable, Equatable {
    let date: Date
    let download: Int64
    let upload: Int64

    var id: Date { date }
}

/// Recent session-wide transfer speeds for the Speed tab's chart.
struct SpeedHistory: Equatable {
    static let window: TimeInterval = 10 * 60

    private(set) var samples: [SpeedSample] = []

    mutating func append(download: Int64, upload: Int64, at date: Date = .now) {
        samples.append(SpeedSample(date: date, download: download, upload: upload))
        let cutoff = date.addingTimeInterval(-Self.window)
        samples.removeAll { $0.date < cutoff }
    }
}
