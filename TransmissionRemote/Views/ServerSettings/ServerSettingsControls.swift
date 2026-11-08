import SwiftUI

// MARK: - Form rows shared by the server settings sections

struct NumberRow: View {
    let title: LocalizedStringResource
    @Binding var value: Int
    let unit: LocalizedStringResource?

    var body: some View {
        HStack {
            Text(title)
            Spacer()
            NumberField(verbatim: String(localized: title), value: $value, format: .number.grouping(.never))
                .labelsHidden()
                .multilineTextAlignment(.trailing)
                .frame(width: 80)
            if let unit { Text(unit).foregroundStyle(.secondary) }
        }
    }
}

struct ToggleNumberRow: View {
    let title: LocalizedStringResource
    @Binding var isOn: Bool
    @Binding var value: Int
    let unit: LocalizedStringResource

    var body: some View {
        LimitRow(title: title, isOn: $isOn, value: $value, unit: unit)
    }
}

/// Edits "minutes after midnight", the unit Transmission uses for the speed schedule.
struct MinutesOfDayPicker: View {
    let title: LocalizedStringResource
    @Binding var minutes: Int

    var body: some View {
        DatePicker(String(localized: title), selection: dateBinding, displayedComponents: .hourAndMinute)
    }

    private var dateBinding: Binding<Date> {
        let midnight = Calendar.current.startOfDay(for: .now)
        return Binding {
            midnight.addingTimeInterval(TimeInterval(minutes * 60))
        } set: { date in
            let components = Calendar.current.dateComponents([.hour, .minute], from: date)
            minutes = (components.hour ?? 0) * 60 + (components.minute ?? 0)
        }
    }
}

/// Transmission's day bitmask: Sunday = 1, Monday = 2, … Saturday = 64.
struct WeekdayPicker: View {
    @Binding var mask: Int

    var body: some View {
        LabeledContent("Days") {
            HStack(spacing: 4) {
                ForEach(0..<7, id: \.self) { index in
                    let bit = 1 << index
                    Toggle(Calendar.current.veryShortWeekdaySymbols[index], isOn: Binding(
                        get: { mask & bit != 0 },
                        set: { mask = $0 ? mask | bit : mask & ~bit }
                    ))
                    .toggleStyle(.button)
                }
            }
        }
    }
}

/// Outcome of the server's port check, shown next to the Test Port button.
enum PortTestResult: Equatable {
    case open
    case closed
    /// The check itself failed, so the port's state is unknown.
    case failed(String)
}

/// Shows a port check result in green (open) or red (closed).
/// The icon repeats the meaning, so it doesn't rely on colour alone.
struct PortTestResultLabel: View {
    let result: PortTestResult

    var body: some View {
        switch result {
        case .open:
            Label("Port is open", systemImage: "checkmark.circle.fill")
                .foregroundStyle(.green)
        case .closed:
            Label("Port is closed", systemImage: "xmark.circle.fill")
                .foregroundStyle(.red)
        case .failed(let message):
            Text(message)
                .foregroundStyle(.secondary)
        }
    }
}
