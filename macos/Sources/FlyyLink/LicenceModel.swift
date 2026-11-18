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

    /// The user opened the card during the trial.
    @Published private(set) var asked = false
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

    /// The sign-in card: always while locked, or after the user asks for it during the trial.
    var showsCard: Bool { isLocked || asked }

    var showsTrialBanner: Bool {
        if case .trial = status { return !asked }

        return false
    }

    var trialText: String {
        guard case .trial(let days) = status else { return "" }

        return "\(days) \(days == 1 ? "day" : "days") left in your trial"
    }

    var licensedTo: String? {
        guard case .licensed(let email, _) = status else { return nil }

        return email
    }

    var canSignIn: Bool {
        switch status {
        case .trial, .locked: return true
        default: return false
        }
    }

    var title: String {
        switch status {
        case .locked(.trialOver): return "Your trial is over"
        case .locked(.offline): return "Go online to continue"
        case .locked(.removed): return "This Mac was removed"
        case .locked(.revoked): return "Your licence isn't active"
        default: return "Sign in"
        }
    }

    var body: String {
        status == .locked(.offline) ? "FlyyLink needs to check your licence." : "Use the email you bought FlyyLink with."
    }

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

    func openCard() { asked = true }

    func closeCard() {
        asked = false
        useAnotherEmail()
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
            closeCard()
        }
    }

    static func buy() { NSWorkspace.shared.open(LicenceServer.site.appendingPathComponent("buy")) }

    static func manageComputers() { NSWorkspace.shared.open(LicenceServer.site.appendingPathComponent("account")) }

    /// Renews the licence or fetches the trial. False when the server couldn't be reached.
    private func sync() async -> Bool {
        do {
            if let secret = store.secret {
                store.renewed(token: try await server.refresh(machine: machine, secret: secret))
            } else if store.record.serverLock == nil || store.record.serverLock == .trialOver,
                      let publicKey, LicenceToken.verify(store.record.trialToken, publicKey: publicKey) == nil {
                store.trialStarted(token: try await server.trial(machine: machine))
            }

            await notices.check(machine: machine, secret: store.secret)

            return true
        } catch let failure as LicenceServer.Failure {
            switch failure.code {
            case "not_activated": store.locked(.removed)
            case "no_licence": store.locked(.revoked)
            case "trial_over": store.locked(.trialOver)
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
                error = Self.describe(failure.code)
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

    private static func describe(_ code: String) -> String {
        switch code {
        case "bad_email": return "Check the email address."
        case "bad_code": return "That code didn't work. Ask for a new one."
        case "too_many_codes": return "Too many codes. Try again in an hour."
        case "no_licence": return "This email has no FlyyLink plan."
        case "computer_limit": return "You're on 5 computers already. Remove one in your account."
        case "offline": return "Couldn't reach FlyyLink. Check your connection."
        default: return "Something went wrong. Try again."
        }
    }
}
