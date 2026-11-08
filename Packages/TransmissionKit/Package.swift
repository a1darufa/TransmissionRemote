// swift-tools-version: 6.2
import PackageDescription

/// Lints every build of both targets with the repository's `.swiftlint.yml`.
let swiftLint: Target.PluginUsage = .plugin(name: "SwiftLintBuildToolPlugin", package: "SwiftLintPlugins")

let package = Package(
    name: "TransmissionKit",
    defaultLocalization: "en",
    platforms: [.macOS(.v26)],
    products: [
        .library(name: "TransmissionKit", targets: ["TransmissionKit"])
    ],
    dependencies: [
        .package(url: "https://github.com/SimplyDanny/SwiftLintPlugins", .upToNextMinor(from: "0.65.1"))
    ],
    targets: [
        .target(
            name: "TransmissionKit",
            resources: [.process("Resources")],
            plugins: [swiftLint]
        ),
        .testTarget(
            name: "TransmissionKitTests",
            dependencies: ["TransmissionKit"],
            plugins: [swiftLint]
        )
    ]
)
