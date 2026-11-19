using System.Text.RegularExpressions;

namespace FlyyLink.Core;

/// Finds the sign-in code in a phone notification, such as "Your verification code is 482 913", so the browser
/// add-on can fill it into the page. Mirrors OneTimeCode.swift.
public static partial class OneTimeCode
{
    /// Text without one of these words is not about a code, whatever numbers it holds.
    private static readonly string[] Keywords =
    [
        "code", "otp", "verif", "passcode", "password", "pin", "2fa", "one-time", "one time", "security",
        "sign in", "sign-in", "signin", "log in", "login", "authenticat", "confirm", "token", "código", "codigo",
    ];

    public static string? Find(string title, string text)
    {
        var message = $"{title}\n{text}";
        var lower = message.ToLowerInvariant();

        var keywords = Keywords.SelectMany(word => Positions(lower, word)).ToList();
        if (keywords.Count == 0) return null;

        var candidates = Candidates(message).Where(c => !LooksLikeSomethingElse(message, c)).ToList();
        if (candidates.Count == 0) return null;

        return candidates
            .OrderBy(c => keywords.Min(k => Math.Abs(k - c.Index)))
            .First()
            .Code;
    }

    private sealed record Candidate(string Code, int Index, int Length);

    private static IEnumerable<Candidate> Candidates(string message)
    {
        foreach (Match match in SplitDigits().Matches(message))
        {
            yield return new Candidate(match.Groups[1].Value + match.Groups[2].Value, match.Index, match.Length);
        }

        foreach (Match match in Digits().Matches(message))
        {
            yield return new Candidate(match.Value, match.Index, match.Length);
        }

        foreach (Match match in AfterCode().Matches(message))
        {
            var code = match.Groups[1].Value;
            if (code.Any(char.IsDigit) && code.Any(char.IsLetter)) yield return new Candidate(code, match.Groups[1].Index, code.Length);
        }
    }

    /// Money, phone numbers, times and dates.
    private static bool LooksLikeSomethingElse(string message, Candidate candidate)
    {
        var before = message[..candidate.Index].TrimEnd();
        var after = message[(candidate.Index + candidate.Length)..];

        if (before.Length > 0 && "+$€£¥₦#".Contains(before[^1])) return true;
        if (before.EndsWith("NGN", StringComparison.OrdinalIgnoreCase) || before.EndsWith("USD", StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.Index > 1 && ".,:/-".Contains(message[candidate.Index - 1]) && char.IsDigit(message[candidate.Index - 2])) return true;
        if (after.Length > 1 && ".,:/-".Contains(after[0]) && char.IsDigit(after[1])) return true;

        return after.StartsWith('%');
    }

    private static IEnumerable<int> Positions(string text, string word)
    {
        for (var index = text.IndexOf(word, StringComparison.Ordinal); index >= 0; index = text.IndexOf(word, index + 1, StringComparison.Ordinal))
        {
            yield return index;
        }
    }

    [GeneratedRegex(@"(?<![\w+])(\d{4,8})(?!\w)")]
    private static partial Regex Digits();

    [GeneratedRegex(@"(?<![\w+\-])(\d{3})[ \-](\d{3})(?![\w\-]|[ \-]\d)")]
    private static partial Regex SplitDigits();

    [GeneratedRegex(@"(?i:code)(?:\s+is)?\s*[:\-]?\s*([A-Za-z0-9]{4,10})\b")]
    private static partial Regex AfterCode();
}
