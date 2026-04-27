using MeowBox.Core.Services;
namespace MeowBox.Core.Models;

public static class StandardKeyGroup
{
    public const string Navigation = "Navigation";
    public const string Editing = "Editing";
    public const string Modifiers = "Modifiers";
    public const string Function = "Function";
    public const string Letters = "Letters";
    public const string Numbers = "Numbers";
    public const string Symbols = "Symbols";
    public const string Numpad = "Numpad";
    public const string Browser = "Browser";
}

public static class KeyChordModifier
{
    public const string Control = "Control";
    public const string Shift = "Shift";
    public const string Alt = "Alt";
    public const string Windows = "Windows";
}

public sealed record StandardKeyGroupOption(string Key, string Label);

public sealed class StandardKeyOption
{
    public StandardKeyOption(string key, string label, ushort virtualKey, string group)
    {
        Key = key;
        Label = label;
        VirtualKey = virtualKey;
        Group = group;
    }

    public string Key { get; }

    public string Label { get; }

    public ushort VirtualKey { get; }

    public string Group { get; }
}

public sealed class KeyChordModifierOption
{
    public KeyChordModifierOption(string key, string label, ushort virtualKey, int sortOrder)
    {
        Key = key;
        Label = label;
        VirtualKey = virtualKey;
        SortOrder = sortOrder;
    }

    public string Key { get; }

    public string Label { get; }

    public ushort VirtualKey { get; }

    public int SortOrder { get; }
}

public static class StandardKeyCatalog
{
    public static IReadOnlyList<StandardKeyGroupOption> GroupOptions { get; } =
    [
        new(StandardKeyGroup.Navigation, ResourceStringService.GetString("StandardKeyGroup.Navigation", "Navigation")),
        new(StandardKeyGroup.Editing, ResourceStringService.GetString("StandardKeyGroup.Editing", "Editing")),
        new(StandardKeyGroup.Modifiers, ResourceStringService.GetString("StandardKeyGroup.Modifiers", "Modifiers")),
        new(StandardKeyGroup.Function, ResourceStringService.GetString("StandardKeyGroup.Function", "Function")),
        new(StandardKeyGroup.Letters, ResourceStringService.GetString("StandardKeyGroup.Letters", "Letters")),
        new(StandardKeyGroup.Numbers, ResourceStringService.GetString("StandardKeyGroup.Numbers", "Numbers")),
        new(StandardKeyGroup.Symbols, ResourceStringService.GetString("StandardKeyGroup.Symbols", "Symbols")),
        new(StandardKeyGroup.Numpad, ResourceStringService.GetString("StandardKeyGroup.Numpad", "Numpad")),
        new(StandardKeyGroup.Browser, ResourceStringService.GetString("StandardKeyGroup.Browser", "Browser"))
    ];

    public static IReadOnlyList<KeyChordModifierOption> ModifierOptions { get; } =
    [
        new(KeyChordModifier.Control, "Ctrl", 0x11, 0),
        new(KeyChordModifier.Shift, "Shift", 0x10, 1),
        new(KeyChordModifier.Alt, "Alt", 0x12, 2),
        new(KeyChordModifier.Windows, ResourceStringService.GetString("KeyChordModifier.Win", "Win"), 0x5B, 3)
    ];

    public static IReadOnlyList<StandardKeyOption> All { get; } =
    [
        new("Escape", "Esc", 0x1B, StandardKeyGroup.Navigation),
        new("Left", ResourceStringService.GetString("StandardKey.LeftArrow", "Left Arrow"), 0x25, StandardKeyGroup.Navigation),
        new("Up", ResourceStringService.GetString("StandardKey.UpArrow", "Up Arrow"), 0x26, StandardKeyGroup.Navigation),
        new("Right", ResourceStringService.GetString("StandardKey.RightArrow", "Right Arrow"), 0x27, StandardKeyGroup.Navigation),
        new("Down", ResourceStringService.GetString("StandardKey.DownArrow", "Down Arrow"), 0x28, StandardKeyGroup.Navigation),
        new("Apps", ResourceStringService.GetString("StandardKey.Apps", "Menu"), 0x5D, StandardKeyGroup.Navigation),
        new("ScrollLock", ResourceStringService.GetString("StandardKey.ScrollLock", "Scroll Lock"), 0x91, StandardKeyGroup.Navigation),
        new("Pause", ResourceStringService.GetString("StandardKey.Pause", "Pause"), 0x13, StandardKeyGroup.Navigation),

        new("Tab", "Tab", 0x09, StandardKeyGroup.Editing),
        new("Enter", "Enter", 0x0D, StandardKeyGroup.Editing),
        new("Space", ResourceStringService.GetString("StandardKey.Space", "Space"), 0x20, StandardKeyGroup.Editing),
        new("Backspace", ResourceStringService.GetString("StandardKey.Backspace", "Backspace"), 0x08, StandardKeyGroup.Editing),
        new("Delete", ResourceStringService.GetString("StandardKey.Delete", "Delete"), 0x2E, StandardKeyGroup.Editing),
        new("Insert", ResourceStringService.GetString("StandardKey.Insert", "Insert"), 0x2D, StandardKeyGroup.Editing),
        new("Home", "Home", 0x24, StandardKeyGroup.Editing),
        new("End", "End", 0x23, StandardKeyGroup.Editing),
        new("PageUp", "Page Up", 0x21, StandardKeyGroup.Editing),
        new("PageDown", "Page Down", 0x22, StandardKeyGroup.Editing),
        new("CapsLock", ResourceStringService.GetString("StandardKey.CapsLock", "Caps Lock"), 0x14, StandardKeyGroup.Editing),
        new("NumLock", ResourceStringService.GetString("StandardKey.NumLock", "Num Lock"), 0x90, StandardKeyGroup.Editing),

        new("Shift", "Shift", 0x10, StandardKeyGroup.Modifiers),
        new("Control", "Ctrl", 0x11, StandardKeyGroup.Modifiers),
        new("Alt", "Alt", 0x12, StandardKeyGroup.Modifiers),
        new("LeftWindows", ResourceStringService.GetString("StandardKey.LeftWindows", "Left Windows"), 0x5B, StandardKeyGroup.Modifiers),
        new("RightWindows", ResourceStringService.GetString("StandardKey.RightWindows", "Right Windows"), 0x5C, StandardKeyGroup.Modifiers),

        new("F1", "F1", 0x70, StandardKeyGroup.Function),
        new("F2", "F2", 0x71, StandardKeyGroup.Function),
        new("F3", "F3", 0x72, StandardKeyGroup.Function),
        new("F4", "F4", 0x73, StandardKeyGroup.Function),
        new("F5", "F5", 0x74, StandardKeyGroup.Function),
        new("F6", "F6", 0x75, StandardKeyGroup.Function),
        new("F7", "F7", 0x76, StandardKeyGroup.Function),
        new("F8", "F8", 0x77, StandardKeyGroup.Function),
        new("F9", "F9", 0x78, StandardKeyGroup.Function),
        new("F10", "F10", 0x79, StandardKeyGroup.Function),
        new("F11", "F11", 0x7A, StandardKeyGroup.Function),
        new("F12", "F12", 0x7B, StandardKeyGroup.Function),
        new("F13", "F13", 0x7C, StandardKeyGroup.Function),
        new("F14", "F14", 0x7D, StandardKeyGroup.Function),
        new("F15", "F15", 0x7E, StandardKeyGroup.Function),
        new("F16", "F16", 0x7F, StandardKeyGroup.Function),
        new("F17", "F17", 0x80, StandardKeyGroup.Function),
        new("F18", "F18", 0x81, StandardKeyGroup.Function),
        new("F19", "F19", 0x82, StandardKeyGroup.Function),
        new("F20", "F20", 0x83, StandardKeyGroup.Function),
        new("F21", "F21", 0x84, StandardKeyGroup.Function),
        new("F22", "F22", 0x85, StandardKeyGroup.Function),
        new("F23", "F23", 0x86, StandardKeyGroup.Function),
        new("F24", "F24", 0x87, StandardKeyGroup.Function),

        new("A", "A", 0x41, StandardKeyGroup.Letters),
        new("B", "B", 0x42, StandardKeyGroup.Letters),
        new("C", "C", 0x43, StandardKeyGroup.Letters),
        new("D", "D", 0x44, StandardKeyGroup.Letters),
        new("E", "E", 0x45, StandardKeyGroup.Letters),
        new("F", "F", 0x46, StandardKeyGroup.Letters),
        new("G", "G", 0x47, StandardKeyGroup.Letters),
        new("H", "H", 0x48, StandardKeyGroup.Letters),
        new("I", "I", 0x49, StandardKeyGroup.Letters),
        new("J", "J", 0x4A, StandardKeyGroup.Letters),
        new("K", "K", 0x4B, StandardKeyGroup.Letters),
        new("L", "L", 0x4C, StandardKeyGroup.Letters),
        new("M", "M", 0x4D, StandardKeyGroup.Letters),
        new("N", "N", 0x4E, StandardKeyGroup.Letters),
        new("O", "O", 0x4F, StandardKeyGroup.Letters),
        new("P", "P", 0x50, StandardKeyGroup.Letters),
        new("Q", "Q", 0x51, StandardKeyGroup.Letters),
        new("R", "R", 0x52, StandardKeyGroup.Letters),
        new("S", "S", 0x53, StandardKeyGroup.Letters),
        new("T", "T", 0x54, StandardKeyGroup.Letters),
        new("U", "U", 0x55, StandardKeyGroup.Letters),
        new("V", "V", 0x56, StandardKeyGroup.Letters),
        new("W", "W", 0x57, StandardKeyGroup.Letters),
        new("X", "X", 0x58, StandardKeyGroup.Letters),
        new("Y", "Y", 0x59, StandardKeyGroup.Letters),
        new("Z", "Z", 0x5A, StandardKeyGroup.Letters),

        new("0", "0", 0x30, StandardKeyGroup.Numbers),
        new("1", "1", 0x31, StandardKeyGroup.Numbers),
        new("2", "2", 0x32, StandardKeyGroup.Numbers),
        new("3", "3", 0x33, StandardKeyGroup.Numbers),
        new("4", "4", 0x34, StandardKeyGroup.Numbers),
        new("5", "5", 0x35, StandardKeyGroup.Numbers),
        new("6", "6", 0x36, StandardKeyGroup.Numbers),
        new("7", "7", 0x37, StandardKeyGroup.Numbers),
        new("8", "8", 0x38, StandardKeyGroup.Numbers),
        new("9", "9", 0x39, StandardKeyGroup.Numbers),

        new("OemSemicolon", ";", 0xBA, StandardKeyGroup.Symbols),
        new("OemPlus", "=", 0xBB, StandardKeyGroup.Symbols),
        new("OemComma", ",", 0xBC, StandardKeyGroup.Symbols),
        new("OemMinus", "-", 0xBD, StandardKeyGroup.Symbols),
        new("OemPeriod", ".", 0xBE, StandardKeyGroup.Symbols),
        new("OemQuestion", "/", 0xBF, StandardKeyGroup.Symbols),
        new("OemTilde", "`", 0xC0, StandardKeyGroup.Symbols),
        new("OemOpenBrackets", "[", 0xDB, StandardKeyGroup.Symbols),
        new("OemPipe", "\\", 0xDC, StandardKeyGroup.Symbols),
        new("OemCloseBrackets", "]", 0xDD, StandardKeyGroup.Symbols),
        new("OemQuotes", "'", 0xDE, StandardKeyGroup.Symbols),

        new("NumPad0", ResourceStringService.GetString("StandardKey.NumPad0", "NumPad 0"), 0x60, StandardKeyGroup.Numpad),
        new("NumPad1", ResourceStringService.GetString("StandardKey.NumPad1", "NumPad 1"), 0x61, StandardKeyGroup.Numpad),
        new("NumPad2", ResourceStringService.GetString("StandardKey.NumPad2", "NumPad 2"), 0x62, StandardKeyGroup.Numpad),
        new("NumPad3", ResourceStringService.GetString("StandardKey.NumPad3", "NumPad 3"), 0x63, StandardKeyGroup.Numpad),
        new("NumPad4", ResourceStringService.GetString("StandardKey.NumPad4", "NumPad 4"), 0x64, StandardKeyGroup.Numpad),
        new("NumPad5", ResourceStringService.GetString("StandardKey.NumPad5", "NumPad 5"), 0x65, StandardKeyGroup.Numpad),
        new("NumPad6", ResourceStringService.GetString("StandardKey.NumPad6", "NumPad 6"), 0x66, StandardKeyGroup.Numpad),
        new("NumPad7", ResourceStringService.GetString("StandardKey.NumPad7", "NumPad 7"), 0x67, StandardKeyGroup.Numpad),
        new("NumPad8", ResourceStringService.GetString("StandardKey.NumPad8", "NumPad 8"), 0x68, StandardKeyGroup.Numpad),
        new("NumPad9", ResourceStringService.GetString("StandardKey.NumPad9", "NumPad 9"), 0x69, StandardKeyGroup.Numpad),
        new("Multiply", ResourceStringService.GetString("StandardKey.Multiply", "NumPad *"), 0x6A, StandardKeyGroup.Numpad),
        new("Add", ResourceStringService.GetString("StandardKey.Add", "NumPad +"), 0x6B, StandardKeyGroup.Numpad),
        new("Subtract", ResourceStringService.GetString("StandardKey.Subtract", "NumPad -"), 0x6D, StandardKeyGroup.Numpad),
        new("Decimal", ResourceStringService.GetString("StandardKey.Decimal", "NumPad ."), 0x6E, StandardKeyGroup.Numpad),
        new("Divide", ResourceStringService.GetString("StandardKey.Divide", "NumPad /"), 0x6F, StandardKeyGroup.Numpad),

        new("BrowserBack", ResourceStringService.GetString("StandardKey.BrowserBack", "Back"), 0xA6, StandardKeyGroup.Browser),
        new("BrowserForward", ResourceStringService.GetString("StandardKey.BrowserForward", "Forward"), 0xA7, StandardKeyGroup.Browser),
        new("BrowserRefresh", ResourceStringService.GetString("StandardKey.BrowserRefresh", "Refresh"), 0xA8, StandardKeyGroup.Browser),
        new("BrowserStop", ResourceStringService.GetString("StandardKey.BrowserStop", "Stop"), 0xA9, StandardKeyGroup.Browser),
        new("BrowserSearch", ResourceStringService.GetString("StandardKey.BrowserSearch", "Search"), 0xAA, StandardKeyGroup.Browser),
        new("BrowserFavorites", ResourceStringService.GetString("StandardKey.BrowserFavorites", "Favorites"), 0xAB, StandardKeyGroup.Browser),
        new("BrowserHome", ResourceStringService.GetString("StandardKey.BrowserHome", "Browser Home"), 0xAC, StandardKeyGroup.Browser)
    ];

    public static StandardKeyOption? GetOption(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return All.FirstOrDefault(option => string.Equals(option.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static KeyChordModifierOption? GetModifierOption(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return ModifierOptions.FirstOrDefault(option => string.Equals(option.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeKey(string? key)
    {
        return GetOption(key)?.Key ?? string.Empty;
    }

    public static string GetLabel(string? key)
    {
        return GetOption(key)?.Label ?? ResourceStringService.GetString("StandardKey.ChoosePrimaryKey", "Choose primary key");
    }

    public static string GetGroupLabel(string? key)
    {
        return GroupOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? GroupOptions[0].Label;
    }

    public static string NormalizeGroupKey(string? key)
    {
        return GroupOptions.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))?.Key
            ?? GroupOptions[0].Key;
    }

    public static string GetPreferredGroupKey(string? key)
    {
        return GetOption(key)?.Group ?? GroupOptions[0].Key;
    }

    public static bool MatchesGroup(StandardKeyOption option, string? group)
    {
        return string.Equals(option.Group, NormalizeGroupKey(group), StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesGroup(string? key, string? group)
    {
        var option = GetOption(key);
        return option is not null && MatchesGroup(option, group);
    }

    public static string NormalizeModifierKey(string? key)
    {
        return GetModifierOption(key)?.Key ?? string.Empty;
    }

    public static IReadOnlyList<string> NormalizeModifierKeys(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            return [];
        }

        return keys
            .Select(NormalizeModifierKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetModifierSortOrder)
            .ToList();
    }

    public static string GetModifierLabel(string? key)
    {
        return GetModifierOption(key)?.Label ?? string.Empty;
    }

    public static bool HasPrimaryKey(KeyChordConfiguration? chord)
    {
        return !string.IsNullOrWhiteSpace(NormalizeKey(chord?.PrimaryKey));
    }

    public static string BuildKeyChordText(string? primaryKey, IEnumerable<string>? modifiers)
    {
        var parts = NormalizeModifierKeys(modifiers)
            .Select(GetModifierLabel)
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .ToList();

        var normalizedPrimaryKey = NormalizeKey(primaryKey);
        if (!string.IsNullOrWhiteSpace(normalizedPrimaryKey))
        {
            parts.Add(GetLabel(normalizedPrimaryKey));
        }

        return string.Join(" + ", parts);
    }

    public static KeyChordConfiguration? NormalizeChord(KeyChordConfiguration? chord)
    {
        if (chord is null)
        {
            return null;
        }

        var normalizedPrimaryKey = NormalizeKey(chord.PrimaryKey);
        var normalizedModifiers = NormalizeModifierKeys(chord.Modifiers);
        if (string.IsNullOrWhiteSpace(normalizedPrimaryKey) && normalizedModifiers.Count == 0)
        {
            return null;
        }

        return new KeyChordConfiguration
        {
            PrimaryKey = string.IsNullOrWhiteSpace(normalizedPrimaryKey) ? null : normalizedPrimaryKey,
            Modifiers = [.. normalizedModifiers]
        };
    }

    public static bool TryGetVirtualKey(string? key, out ushort virtualKey)
    {
        if (GetOption(key) is { } option)
        {
            virtualKey = option.VirtualKey;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    public static bool TryGetModifierVirtualKey(string? key, out ushort virtualKey)
    {
        if (GetModifierOption(key) is { } option)
        {
            virtualKey = option.VirtualKey;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static int GetModifierSortOrder(string key)
    {
        return GetModifierOption(key)?.SortOrder ?? int.MaxValue;
    }
}
