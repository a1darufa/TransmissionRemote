import Charts
import SwiftUI

/// Session-wide transfer speed over the last few minutes.
struct SpeedTab: View {
    let history: SpeedHistory

    var body: some View {
        if history.samples.count < 2 {
            ContentUnavailableView("Collecting Speed Data", systemImage: "chart.xyaxis.line")
        } else {
            Chart {
                ForEach(history.samples) { sample in
                    LineMark(x: .value("Time", sample.date), y: .value("Speed", sample.download))
                        .foregroundStyle(by: .value("Direction", String(localized: "Download")))
                    LineMark(x: .value("Time", sample.date), y: .value("Speed", sample.upload))
                        .foregroundStyle(by: .value("Direction", String(localized: "Upload")))
                }
            }
            .chartForegroundStyleScale([
                String(localized: "Download"): Color.blue,
                String(localized: "Upload"): Color.green
            ])
            .chartYAxis {
                AxisMarks { value in
                    AxisGridLine()
                    AxisValueLabel {
                        if let bytes = value.as(Int64.self) { Text(bytes.formattedSpeed) }
                    }
                }
            }
            .padding()
        }
    }
}
