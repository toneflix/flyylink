import Foundation

/// Talks to the licence API on the FlyyLink website.
struct LicenceServer {
    static let site = URL(string: "https://flyylink.toneflix.net")!

    /// An error code from the server, such as "bad_code" or "computer_limit".
    struct Failure: Error {
        let code: String
    }

    struct Activation: Decodable {
        let token: String
        let secret: String
    }

    struct Notice: Decodable {
        let id: Int
        let title: String
        let message: String
        let link: String?
    }

    struct Notices: Decodable {
        let notices: [Notice]
        let latest: Int
    }

    private struct TokenReply: Decodable {
        let token: String
    }

    private struct ErrorReply: Decodable {
        let error: String?
    }

    func sendCode(to email: String) async throws {
        _ = try await post("/api/code", ["email": email])
    }

    func activate(email: String, code: String, machine: String, name: String) async throws -> Activation {
        let body: [String: Any] = ["email": email, "code": code, "machine": ["id": machine, "name": name, "platform": "mac"]]

        return try await post("/api/activate", body, as: Activation.self)
    }

    func refresh(machine: String, secret: String) async throws -> String {
        try await post("/api/refresh", ["machine": machine, "secret": secret], as: TokenReply.self).token
    }

    /// App notices newer than `after`. With no `after`, only the newest id, to start from.
    func notices(machine: String, secret: String?, after: Int?) async throws -> Notices {
        var body: [String: Any] = ["machine": machine]
        if let secret { body["secret"] = secret }
        if let after { body["after"] = after }

        return try await post("/api/notices", body, as: Notices.self)
    }

    private func post<T: Decodable>(_ path: String, _ body: [String: Any], as type: T.Type) async throws -> T {
        try JSONDecoder().decode(type, from: try await post(path, body))
    }

    private func post(_ path: String, _ body: [String: Any]) async throws -> Data {
        var request = URLRequest(url: Self.site.appendingPathComponent(path), timeoutInterval: 20)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)

        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            throw Failure(code: (try? JSONDecoder().decode(ErrorReply.self, from: data))?.error ?? "server_error")
        }

        return data
    }
}
