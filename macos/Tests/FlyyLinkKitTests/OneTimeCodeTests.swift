import XCTest
@testable import FlyyLinkKit

/// Mirrors OneTimeCodeTests.cs.
final class OneTimeCodeTests: XCTestCase {
    func testFindsTheCode() {
        let cases: [(String, String, String)] = [
            ("Messages", "Your verification code is 482913. Don't share it with anyone.", "482913"),
            ("Google", "G-731562 is your Google verification code.", "731562"),
            ("Bank", "Use 482 913 to sign in. It expires in 10 minutes.", "482913"),
            ("WhatsApp", "Your WhatsApp code: 123-456", "123456"),
            ("Shop", "Your login code is K7Q2PX", "K7Q2PX"),
            ("Bank", "NGN 5,000 was paid on 19/11/2026. Your OTP for the next payment is 8812", "8812"),
            ("Microsoft", "Security code: 0042", "0042"),
            ("2FA", "Order 55190 shipped. Your one-time passcode is 774201", "774201"),
        ]

        for (title, text, code) in cases {
            XCTAssertEqual(OneTimeCode.find(title: title, text: text), code, text)
        }
    }

    func testIgnoresMessagesWithoutACode() {
        let cases: [(String, String)] = [
            ("Mum", "Call me on 08031234567 when you land"),
            ("Bank", "You paid $1500 at the market"),
            ("Calendar", "Lunch at 12:30 on 2026-11-19"),
            ("Shop", "Your code expires tomorrow"),
            ("Phone", "Your code was sent to +2348031234567"),
            ("News", "48291 people read this today"),
        ]

        for (title, text) in cases {
            XCTAssertNil(OneTimeCode.find(title: title, text: text), text)
        }
    }
}
