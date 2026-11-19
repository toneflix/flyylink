import FlyyLinkKit
import Foundation
import IOKit
import Security

/// The licence tokens in UserDefaults and the refresh secret in the Keychain.
struct LicenceStore {
    private static let recordKey = "licenceRecord"
    private static let secretAccount = "licence-secret"
    private static let service = "com.toneflix.flyylink"

    var record: LicenceRecord {
        get {
            if let data = UserDefaults.standard.data(forKey: Self.recordKey),
               let record = try? JSONDecoder().decode(LicenceRecord.self, from: data) {
                return record
            }

            let fresh = LicenceRecord()
            UserDefaults.standard.set(try? JSONEncoder().encode(fresh), forKey: Self.recordKey)

            return fresh
        }
        nonmutating set {
            UserDefaults.standard.set(try? JSONEncoder().encode(newValue), forKey: Self.recordKey)
        }
    }

    var secret: String? {
        get {
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: Self.service,
                kSecAttrAccount as String: Self.secretAccount,
                kSecReturnData as String: true,
            ]
            var result: AnyObject?
            guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess, let data = result as? Data else { return nil }

            return String(data: data, encoding: .utf8)
        }
        nonmutating set {
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: Self.service,
                kSecAttrAccount as String: Self.secretAccount,
            ]
            SecItemDelete(query as CFDictionary)
            guard let newValue else { return }

            var item = query
            item[kSecValueData as String] = Data(newValue.utf8)
            item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            SecItemAdd(item as CFDictionary, nil)
        }
    }

    func activated(token: String, secret: String) {
        self.secret = secret
        record.licenceToken = token
        record.serverLock = nil
    }

    func renewed(token: String) {
        record.licenceToken = token
    }

    /// The server turned this Mac away; forget its licence.
    func locked(_ reason: LicenceLock) {
        secret = nil
        record.licenceToken = nil
        record.serverLock = reason
    }

    /// The Mac's hardware UUID, which stays the same when FlyyLink is reinstalled.
    static func hardwareId() -> String {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPlatformExpertDevice"))
        defer { IOObjectRelease(service) }

        let uuid = IORegistryEntryCreateCFProperty(service, "IOPlatformUUID" as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? String

        return uuid ?? Host.current().localizedName ?? "mac"
    }
}
