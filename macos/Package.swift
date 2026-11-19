// swift-tools-version:5.9
import Foundation
import PackageDescription

// Shortcuts finds the app's actions through metadata built from compiler output Xcode normally asks for.
// These flags ask SwiftPM's compiler and linker for the same files; bundle.sh hands them to Apple's tool.
let packageDir = URL(fileURLWithPath: #filePath).deletingLastPathComponent().path
let intentsFlags = ["-emit-const-values", "-Xfrontend", "-const-gather-protocols-file", "-Xfrontend", "\(packageDir)/AppIntentsProtocols.json"]

let package = Package(
    name: "FlyyLink",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "FlyyLink", targets: ["FlyyLink"]),
        .library(name: "FlyyLinkKit", targets: ["FlyyLinkKit"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-nio.git", from: "2.65.0"),
        .package(url: "https://github.com/apple/swift-nio-ssl.git", from: "2.27.0"),
        .package(url: "https://github.com/apple/swift-certificates.git", from: "1.5.0"),
        .package(url: "https://github.com/apple/swift-asn1.git", from: "1.1.0"),
        .package(url: "https://github.com/apple/swift-crypto.git", "3.0.0"..<"4.0.0"),
        .package(url: "https://github.com/sparkle-project/Sparkle.git", from: "2.6.0"),
    ],
    targets: [
        // Protocol and networking. No UI, so it can be tested on its own.
        .target(
            name: "FlyyLinkKit",
            dependencies: [
                .product(name: "NIOCore", package: "swift-nio"),
                .product(name: "NIOPosix", package: "swift-nio"),
                .product(name: "NIOTLS", package: "swift-nio"),
                .product(name: "NIOSSL", package: "swift-nio-ssl"),
                .product(name: "X509", package: "swift-certificates"),
                .product(name: "SwiftASN1", package: "swift-asn1"),
                .product(name: "Crypto", package: "swift-crypto"),
                .product(name: "_CryptoExtras", package: "swift-crypto"),
            ]
        ),
        // The menu bar app.
        .executableTarget(
            name: "FlyyLink",
            dependencies: ["FlyyLinkKit", .product(name: "Sparkle", package: "Sparkle")],
            swiftSettings: [.unsafeFlags(intentsFlags)],
            linkerSettings: [
                .linkedFramework("AppIntents"),
                .unsafeFlags([
                    "-Xlinker", "-dependency_info", "-Xlinker", "\(packageDir)/.build/FlyyLink_dependency_info.dat",
                    // Sparkle.framework is copied into Contents/Frameworks by bundle.sh.
                    "-Xlinker", "-rpath", "-Xlinker", "@executable_path/../Frameworks",
                ]),
            ]
        ),
        // The native messaging host browsers start for the add-on. bundle.sh puts it next to the app.
        .executableTarget(name: "FlyyLinkBrowserHost", dependencies: ["FlyyLinkKit"]),
        .testTarget(name: "FlyyLinkKitTests", dependencies: ["FlyyLinkKit"]),
    ]
)
