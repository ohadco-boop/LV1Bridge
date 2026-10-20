// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "LV1BridgeMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "LV1BridgeMac", targets: ["LV1BridgeMac"])],
    targets: [
        .executableTarget(name: "LV1BridgeMac"),
        .testTarget(name: "LV1BridgeMacTests", dependencies: ["LV1BridgeMac"])
    ]
)
