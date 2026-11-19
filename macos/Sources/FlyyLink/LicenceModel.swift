import AppKit
import FlyyLinkKit
import Foundation

/// Whether this Mac may use FlyyLink, and the sign-in card that unlocks it. Mirrors LicenceModel.cs.
@MainActor
final class LicenceModel: ObservableObject {
    private static let syncEvery: TimeInterval = 6 * 60 * 60
    private static let checkEvery: UInt64 = 10 * 60 * 1_000_000_000

    @Published private(set) var status: LicenceStatus = .unlimited {
        didSet {
            if status.isLocked != oldValue.isLocked { onLockChange?() }
        }
    }

    @Published private(set) var sentCode = false
    @Published private(set) var isBusy = false
    @Published private(set) var error: String?
    @Published var email = ""
    @Published var code = ""

    /// Called when the Mac becomes locked or unlocked.
    var onLockChange: (() -> Void)?

    private let store = LicenceStore()
    private let server = LicenceServer()
    private lazy var notices = ServerNotices(server: server)
    private let machine = MachineId.from(LicenceStore.hardwareId())
    private let publicKey = Bundle.main.object(forInfoDictionaryKey: "FlyyLinkLicenceKey") as? String
    private var lastSync = Date.distantPast

    init() {
        evaluate()
    }

    var isLocked: Bool { status.isLocked }

    /// The sign-in card, shown in place of the panel while locked.
    var showsCard: Bool { isLocked }

    var licensedTo: String? {
        guard case .licensed(let email, _) = status else { return nil }

        return email
    }

    var canSignIn: Bool { isLocked }

    var title: String {
        switch status {
        case .locked(.offline): return "Go online to continue"
        case .locked(.removed): return "This Mac was removed"
        case .locked(.revoked): return "Your plan has ended"
        default: return "Sign in"
        }
    }

    var body: String {
        switch status {
        case .locked(.offline): return "FlyyLink needs to check your licence."
        case .locked(.signedOut): return "Start your free trial on our website, then sign in here with the same email."
        default: return "Sign in with the email you used for your plan."
        }
    }

    /// The website button: a trial for someone new, otherwise the plans.
    var buyTitle: String { status == .locked(.signedOut) ? "Try it free" : "Buy FlyyLink" }

    var canRetry: Bool { status == .locked(.offline) }

    /// Checks in with the server now, then every few hours, and re-reads the tokens in between.
    func start() {
        guard status != .unlimited else { return }

        Task { [weak self] in
            while let self {
                if Date().timeIntervalSince(self.lastSync) >= Self.syncEvery, await self.sync() { self.lastSync = Date() }
                self.evaluate()
                try? await Task.sleep(nanoseconds: Self.checkEvery)
            }
        }
    }

    func useAnotherEmail() {
        sentCode = false
        code = ""
        error = nil
    }

    func retry() {
        run { [self] in
            if !(await sync()) { throw URLError(.notConnectedToInternet) }
        }
    }

    func sendCode() {
        run { [self] in
            try await server.sendCode(to: email.trimmingCharacters(in: .whitespaces))
            sentCode = true
        }
    }

    func activate() {
        run { [self] in
            let activation = try await server.activate(
                email: email.trimmingCharacters(in: .whitespaces),
                code: code.trimmingCharacters(in: .whitespaces),
                machine: machine,
                name: Host.current().localizedName ?? "Mac"
            )
            store.activated(token: activation.token, secret: activation.secret)
            useAnotherEmail()
        }
    }

    static func buy() { NSWorkspace.shared.open(LicenceServer.site.appendingPathComponent("buy")) }

    static func manageComputers() { NSWorkspace.shared.open(LicenceServer.site.appendingPathComponent("account")) }

    /// Renews the licence. False when the server couldn't be reached.
    private func sync() async -> Bool {
        do {
            if let secret = store.secret {
                store.renewed(token: try await server.refresh(machine: machine, secret: secret))
            }

            await notices.check(machine: machine, secret: store.secret)

            return true
        } catch let failure as LicenceServer.Failure {
            switch failure.code {
            case "not_activated": store.locked(.removed)
            case "no_licence": store.locked(.revoked)
            default: break
            }

            await notices.check(machine: machine, secret: store.secret)

            return true
        } catch {
            return false
        }
    }

    private func run(_ action: @escaping @MainActor () async throws -> Void) {
        isBusy = true
        error = nil

        Task { [self] in
            do {
                try await action()
            } catch let failure as LicenceServer.Failure {
                error = Self.describe(failure.code, limit: failure.limit)
            } catch {
                self.error = Self.describe("offline")
            }

            isBusy = false
            evaluate()
        }
    }

    private func evaluate() {
        status = LicenceRules.evaluate(store.record, machine: machine, publicKey: publicKey, now: Date())
    }

    private static func describe(_ code: String, limit: Int? = nil) -> String {
        switch code {
        case "bad_email": return "Please check the email address."
        case "bad_code": return "That code didn't work. Ask for a new one."
        case "too_many_codes": return "That's a lot of codes. Please try again in an hour."
        case "no_licence": return "We couldn't find a FlyyLink plan for this email."
        case "computer_limit":
            let computers = limit.map { $0 == 1 ? "a computer" : "\($0) computers" } ?? "all the computers your plan covers"
            return "You're already using FlyyLink on \(computers). Remove one in your account to add this one."
        case "offline": return "We couldn't reach FlyyLink. Please check your internet connection."
        default: return "Something went wrong. Please try again."
        }
    }
}
