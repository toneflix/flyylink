import AppKit
import SwiftUI

@main
struct FlyyLinkApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model = AppModel.shared

    var body: some Scene {
        MenuBarExtra {
            MenuPanel(model: model)
        } label: {
            Image(nsImage: .flyyLinkMenuBar)
        }
        .menuBarExtraStyle(.window)

        Window("Messages", id: FlyyLinkApp.messagesWindow) {
            MessagesWindow(store: model.messages, model: model)
        }
        .defaultSize(width: 900, height: 620)
        .handlesExternalEvents(matching: [])

        Window("Photos", id: FlyyLinkApp.photosWindow) {
            PhotosWindow(store: model.photos, model: model)
        }
        .defaultSize(width: 860, height: 640)
        .handlesExternalEvents(matching: [])

        Window("Settings", id: FlyyLinkApp.settingsWindow) {
            SettingsWindow(model: model)
        }
        .windowResizability(.contentSize)
        .handlesExternalEvents(matching: [])

        Window("Call Audio", id: FlyyLinkApp.callAudioWindow) {
            CallAudioWindow(audio: model.callAudio)
        }
        .windowResizability(.contentSize)
        .handlesExternalEvents(matching: [])
    }

    static let messagesWindow = "messages"
    static let photosWindow = "photos"
    static let callAudioWindow = "callAudio"
    static let settingsWindow = "settings"
}

/// Opens links handed to the app: flyylink://share from the Share menu, flyylink://open from the widget,
/// flyylink://ring and flyylink://mirror for scripts, and tel: numbers, such as one clicked in Safari.
/// The windows match no links, so SwiftUI hands every link here instead of opening a window for it.
final class AppDelegate: NSObject, NSApplicationDelegate {
    func application(_ application: NSApplication, open urls: [URL]) {
        Task { @MainActor in
            let model = AppModel.shared

            for url in urls {
                switch url.scheme?.lowercased() {
                case ShareRequest.scheme:
                    switch url.host?.lowercased() {
                    case "open":
                        MenuBarPanel.toggle()
                    case "ring":
                        if let id = model.primaryId { model.ring(id) }
                    case "mirror":
                        if let id = model.primaryId { model.mirror(id) }
                    default:
                        model.shareInbox.open(url)
                    }
                case "tel":
                    let number = (url.absoluteString.dropFirst("tel:".count).removingPercentEncoding ?? "")
                        .filter { $0.isNumber || $0 == "+" || $0 == "*" || $0 == "#" }
                    model.dial(number, name: nil)
                default:
                    break
                }
            }
        }
    }
}
