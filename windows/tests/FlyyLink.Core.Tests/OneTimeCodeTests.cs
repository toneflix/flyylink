namespace FlyyLink.Core.Tests;

/// Mirrors OneTimeCodeTests.swift.
public class OneTimeCodeTests
{
    [Theory]
    [InlineData("Messages", "Your verification code is 482913. Don't share it with anyone.", "482913")]
    [InlineData("Google", "G-731562 is your Google verification code.", "731562")]
    [InlineData("Bank", "Use 482 913 to sign in. It expires in 10 minutes.", "482913")]
    [InlineData("WhatsApp", "Your WhatsApp code: 123-456", "123456")]
    [InlineData("Shop", "Your login code is K7Q2PX", "K7Q2PX")]
    [InlineData("Bank", "NGN 5,000 was paid on 19/11/2026. Your OTP for the next payment is 8812", "8812")]
    [InlineData("Microsoft", "Security code: 0042", "0042")]
    [InlineData("2FA", "Order 55190 shipped. Your one-time passcode is 774201", "774201")]
    public void FindsTheCode(string title, string text, string code) => Assert.Equal(code, OneTimeCode.Find(title, text));

    [Theory]
    [InlineData("Mum", "Call me on 08031234567 when you land")]
    [InlineData("Bank", "You paid $1500 at the market")]
    [InlineData("Calendar", "Lunch at 12:30 on 2026-11-19")]
    [InlineData("Shop", "Your code expires tomorrow")]
    [InlineData("Phone", "Your code was sent to +2348031234567")]
    [InlineData("News", "48291 people read this today")]
    public void IgnoresMessagesWithoutACode(string title, string text) => Assert.Null(OneTimeCode.Find(title, text));
}
