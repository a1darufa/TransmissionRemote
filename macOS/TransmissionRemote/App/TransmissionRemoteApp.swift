import SwiftUI

enum SceneID {
    static let main = "main"
}

@main
struct TransmissionRemoteApp: App {
    @State private var model: AppModel
    @AppStorage(AppSettings.showMenuBarExtra) private var showsMenuBarExtra = true

    init() {
        AppSettings.registerDefaults()
        let model = AppModel()
        _model = State(initialValue: model)
        model.start()
    }

    var body: some Scene {
        Window("Transmission Remote", id: SceneID.main) {
            MainWindow()
                .environment(model)
        }
        .defaultSize(width: 1280, height: 820)
        .handlesExternalEvents(matching: ["*"])
        .commands {
            AppCommands(model: model)
        }

        Settings {
            SettingsView()
                .environment(model)
        }

        MenuBarExtra(isInserted: $showsMenuBarExtra) {
            MenuBarContent(model: model)
        } label: {
            MenuBarLabel(model: model)
        }
    }
}
