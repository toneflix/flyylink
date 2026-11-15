namespace FlyyLink.Shell;

/// Maps the W3C key names that travel between computers to and from Windows virtual keys.
/// `Extended` keys are the ones Windows marks with the extended flag, which tells Numpad Enter from Enter.
internal static class WindowsKeys
{
    public readonly record struct Key(ushort Vk, bool Extended);

    private static readonly Dictionary<string, Key> Codes = Build();

    private static readonly Dictionary<Key, string> Names = Codes
        .GroupBy(pair => pair.Value)
        .ToDictionary(group => group.Key, group => group.First().Key);

    public static Key? Lookup(string code) => Codes.TryGetValue(code, out var key) ? key : null;

    public static string? Name(uint vk, bool extended) =>
        Names.TryGetValue(new Key((ushort)vk, extended), out var name) ? name
        : Names.TryGetValue(new Key((ushort)vk, !extended), out var other) ? other
        : null;

    private static Dictionary<string, Key> Build()
    {
        var map = new Dictionary<string, Key>();
        void Add(string code, int vk, bool extended = false) => map[code] = new Key((ushort)vk, extended);

        for (var c = 'A'; c <= 'Z'; c++) Add($"Key{c}", c);
        for (var d = 0; d <= 9; d++) Add($"Digit{d}", '0' + d);
        for (var f = 1; f <= 12; f++) Add($"F{f}", 0x6F + f);
        for (var n = 0; n <= 9; n++) Add($"Numpad{n}", 0x60 + n);

        Add("NumpadMultiply", 0x6A);
        Add("NumpadAdd", 0x6B);
        Add("NumpadSubtract", 0x6D);
        Add("NumpadDecimal", 0x6E);
        Add("NumpadDivide", 0x6F, extended: true);
        Add("NumpadEnter", 0x0D, extended: true);
        Add("Enter", 0x0D);
        Add("Escape", 0x1B);
        Add("Backspace", 0x08);
        Add("Tab", 0x09);
        Add("Space", 0x20);
        Add("Minus", 0xBD);
        Add("Equal", 0xBB);
        Add("BracketLeft", 0xDB);
        Add("BracketRight", 0xDD);
        Add("Backslash", 0xDC);
        Add("Semicolon", 0xBA);
        Add("Quote", 0xDE);
        Add("Backquote", 0xC0);
        Add("Comma", 0xBC);
        Add("Period", 0xBE);
        Add("Slash", 0xBF);
        Add("IntlBackslash", 0xE2);
        Add("CapsLock", 0x14);
        Add("ArrowLeft", 0x25, extended: true);
        Add("ArrowUp", 0x26, extended: true);
        Add("ArrowRight", 0x27, extended: true);
        Add("ArrowDown", 0x28, extended: true);
        Add("PageUp", 0x21, extended: true);
        Add("PageDown", 0x22, extended: true);
        Add("End", 0x23, extended: true);
        Add("Home", 0x24, extended: true);
        Add("Insert", 0x2D, extended: true);
        Add("Delete", 0x2E, extended: true);
        Add("ContextMenu", 0x5D, extended: true);
        Add("ShiftLeft", 0xA0);
        Add("ShiftRight", 0xA1);
        Add("ControlLeft", 0xA2);
        Add("ControlRight", 0xA3, extended: true);
        Add("AltLeft", 0xA4);
        Add("AltRight", 0xA5, extended: true);
        Add("MetaLeft", 0x5B, extended: true);
        Add("MetaRight", 0x5C, extended: true);
        Add("AudioVolumeMute", 0xAD, extended: true);
        Add("AudioVolumeDown", 0xAE, extended: true);
        Add("AudioVolumeUp", 0xAF, extended: true);
        Add("MediaTrackNext", 0xB0, extended: true);
        Add("MediaTrackPrevious", 0xB1, extended: true);
        Add("MediaPlayPause", 0xB3, extended: true);

        return map;
    }
}
