import Foundation

/// Finds the sign-in code in a phone notification, such as "Your verification code is 482 913", so the browser
/// add-on can fill it into the page. Mirrors OneTimeCode.cs.
public enum OneTimeCode {
    /// Text without one of these words is not about a code, whatever numbers it holds.
    private static let keywords = [
        "code", "otp", "verif", "passcode", "password", "pin", "2fa", "one-time", "one time", "security",
        "sign in", "sign-in", "signin", "log in", "login", "authenticat", "confirm", "token", "código", "codigo",
    ]

    private static let digits = try! NSRegularExpression(pattern: #"(?<![\w+])(\d{4,8})(?!\w)"#)
    private static let splitDigits = try! NSRegularExpression(pattern: #"(?<![\w+\-])(\d{3})[ \-](\d{3})(?![\w\-]|[ \-]\d)"#)
    private static let afterCode = try! NSRegularExpression(pattern: #"(?i:code)(?:\s+is)?\s*[:\-]?\s*([A-Za-z0-9]{4,10})\b"#)

    private struct Candidate {
        let code: String
        /// Where the code starts, in UTF-16 units.
        let index: Int
        let length: Int
    }

    public static func find(title: String, text: String) -> String? {
        let message = "\(title)\n\(text)"
        let lower = message.lowercased() as NSString

        let places = keywords.flatMap { positions(of: $0, in: lower) }
        guard !places.isEmpty else { return nil }

        let codes = candidates(in: message).filter { !looksLikeSomethingElse(message as NSString, $0) }
        guard !codes.isEmpty else { return nil }

        let distance = { (candidate: Candidate) in places.map { abs($0 - candidate.index) }.min() ?? .max }

        return codes.enumerated()
            .min { a, b in (distance(a.element), a.offset) < (distance(b.element), b.offset) }?
            .element.code
    }

    private static func candidates(in message: String) -> [Candidate] {
        let text = message as NSString
        let range = NSRange(location: 0, length: text.length)
        var found: [Candidate] = []

        for match in splitDigits.matches(in: message, range: range) {
            let code = text.substring(with: match.range(at: 1)) + text.substring(with: match.range(at: 2))
            found.append(Candidate(code: code, index: match.range.location, length: match.range.length))
        }
        for match in digits.matches(in: message, range: range) {
            found.append(Candidate(code: text.substring(with: match.range), index: match.range.location, length: match.range.length))
        }
        for match in afterCode.matches(in: message, range: range) {
            let group = match.range(at: 1)
            let code = text.substring(with: group)
            if code.contains(where: \.isNumber), code.contains(where: \.isLetter) {
                found.append(Candidate(code: code, index: group.location, length: group.length))
            }
        }

        return found
    }

    /// Money, phone numbers, times and dates.
    private static func looksLikeSomethingElse(_ message: NSString, _ candidate: Candidate) -> Bool {
        let before = message.substring(to: candidate.index).trimmingCharacters(in: .whitespacesAndNewlines)
        let after = message.substring(from: candidate.index + candidate.length)
        let character = { (index: Int) -> Character? in
            guard index >= 0, index < message.length else { return nil }
            return Character(message.substring(with: NSRange(location: index, length: 1)))
        }

        if let last = before.last, "+$€£¥₦#".contains(last) { return true }
        if before.uppercased().hasSuffix("NGN") || before.uppercased().hasSuffix("USD") { return true }
        if let separator = character(candidate.index - 1), ".,:/-".contains(separator), character(candidate.index - 2)?.isNumber == true { return true }
        if let next = after.first, ".,:/-".contains(next), after.dropFirst().first?.isNumber == true { return true }

        return after.hasPrefix("%")
    }

    private static func positions(of word: String, in text: NSString) -> [Int] {
        var found: [Int] = []
        var range = NSRange(location: 0, length: text.length)

        while true {
            let match = text.range(of: word, options: .literal, range: range)
            guard match.location != NSNotFound else { return found }

            found.append(match.location)
            range = NSRange(location: match.location + 1, length: text.length - match.location - 1)
        }
    }
}
