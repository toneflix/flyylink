import Crypto
import Foundation

/// What the licence server signed. Times are Unix seconds. Mirrors Licence.cs.
public struct LicenceClaims: Codable, Equatable, Sendable {
    public static let licence = "licence"

    public let kind: String
    public let machine: String
    public let email: String?
    public let issued: Int64
    public let renew: Int64
    public let expires: Int64

    public init(kind: String, machine: String, email: String?, issued: Int64, renew: Int64, expires: Int64) {
        self.kind = kind
        self.machine = machine
        self.email = email
        self.issued = issued
        self.renew = renew
        self.expires = expires
    }
}

/// Checks tokens from the licence server: `base64url(json).base64url(r||s)`, signed with ECDSA P-256.
public enum LicenceToken {
    /// The claims, or nil when the token is malformed or not signed by `publicKey` (base64 SubjectPublicKeyInfo).
    public static func verify(_ token: String?, publicKey: String) -> LicenceClaims? {
        guard let parts = token?.split(separator: ".", omittingEmptySubsequences: false), parts.count == 2,
              let keyData = Data(base64Encoded: publicKey),
              let key = try? P256.Signing.PublicKey(derRepresentation: keyData),
              let body = fromBase64URL(String(parts[0])),
              let signatureData = fromBase64URL(String(parts[1])),
              let signature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData),
              key.isValidSignature(signature, for: Data(parts[0].utf8))
        else { return nil }

        return try? JSONDecoder().decode(LicenceClaims.self, from: body)
    }

    private static func fromBase64URL(_ text: String) -> Data? {
        var plain = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        plain += String(repeating: "=", count: (4 - plain.count % 4) % 4)

        return Data(base64Encoded: plain)
    }
}

/// Why the computer apps stopped connecting.
public enum LicenceLock: String, Codable, Sendable {
    /// Not signed in yet. Trials start with a plan bought on the website.
    case signedOut
    /// The token ran out without a renewal; going online once fixes it.
    case offline
    /// The computer was removed from the account on the website.
    case removed
    /// The plan ended, the trial was cancelled or didn't convert, or the purchase was refunded.
    case revoked
}

public enum LicenceStatus: Equatable, Sendable {
    /// A build without a licence key, such as a local one: nothing is checked.
    case unlimited
    case licensed(email: String, shouldRenew: Bool)
    case locked(LicenceLock)

    public var isLocked: Bool {
        if case .locked = self { return true }

        return false
    }
}

/// What a computer app saved from the licence server.
public struct LicenceRecord: Codable, Equatable, Sendable {
    public var licenceToken: String?
    public var serverLock: LicenceLock?

    public init(licenceToken: String? = nil, serverLock: LicenceLock? = nil) {
        self.licenceToken = licenceToken
        self.serverLock = serverLock
    }
}

/// Decides, offline, whether this computer may use FlyyLink. Mirrors LicenceRules in Licence.cs.
public enum LicenceRules {
    public static func evaluate(_ record: LicenceRecord, machine: String, publicKey: String?, now: Date) -> LicenceStatus {
        guard let publicKey, !publicKey.isEmpty else { return .unlimited }

        let seconds = Int64(now.timeIntervalSince1970)
        let licence = LicenceToken.verify(record.licenceToken, publicKey: publicKey).flatMap { claims in
            claims.kind == LicenceClaims.licence && claims.machine == machine ? claims : nil
        }
        if let licence, seconds < licence.expires { return .licensed(email: licence.email ?? "", shouldRenew: seconds >= licence.renew) }
        if let lock = record.serverLock { return .locked(lock) }

        return .locked(licence == nil ? .signedOut : .offline)
    }
}

/// The computer's hardware ID, scrambled so the licence server never sees the real one.
public enum MachineId {
    public static func from(_ hardwareId: String) -> String {
        let text = "flyylink:\(hardwareId.trimmingCharacters(in: .whitespacesAndNewlines).lowercased())"

        return SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined()
    }
}
