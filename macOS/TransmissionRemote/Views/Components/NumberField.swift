import SwiftUI

/// A number field whose value follows every keystroke.
///
/// `TextField(value:format:)` only writes the number when editing ends: Return, Tab or a focus
/// change. On macOS, clicking a button doesn't end editing, so typing a new port or speed limit
/// and clicking Save or Apply with the mouse sent the old number (transgui issues #1326, #1493).
/// Text that doesn't parse (an empty field, a stray letter) leaves the last good number in place.
struct NumberField<Format: ParseableFormatStyle>: View
where Format.FormatOutput == String, Format.FormatInput: Equatable {
    private let label: Text
    @Binding private var value: Format.FormatInput
    private let format: Format
    @State private var text = ""

    init(_ title: LocalizedStringKey, value: Binding<Format.FormatInput>, format: Format) {
        self.label = Text(title)
        self._value = value
        self.format = format
    }

    /// For titles that are already localized, such as ones passed into a reusable row.
    init(verbatim title: String, value: Binding<Format.FormatInput>, format: Format) {
        self.label = Text(verbatim: title)
        self._value = value
        self.format = format
    }

    var body: some View {
        TextField(text: $text) { label }
            .onAppear { text = format.format(value) }
            .onChange(of: text) { _, newText in
                if let parsed = try? format.parseStrategy.parse(newText), parsed != value {
                    value = parsed
                }
            }
            .onChange(of: value) { _, newValue in
                // Changed from outside (a preset, a reload): show it, unless the text already says so.
                if (try? format.parseStrategy.parse(text)) != newValue {
                    text = format.format(newValue)
                }
            }
    }
}
