// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "HaloNotch",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(name: "HaloNotch", path: "Sources/HaloNotch")
    ]
)
