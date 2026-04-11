namespace FnMappingTool.Core.Models;

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

public sealed record StandardKeyGroupOption(string Key, string Label);

public sealed class StandardKeyOption
{
    public StandardKeyOption(string key, string label, int virtualKey, string group)
    {
        Key = key;
        Label = label;
        VirtualKey = virtualKey;
        Group = group;
    }

    public string Key { get; }

    public string Label { get; }

    public int VirtualKey { get; }

    public string Group { get; }
}

public static class StandardKeyCatalog
{
    public static IReadOnlyList<StandardKeyGroupOption> GroupOptions { get; } =
    [
        new(StandardKeyGroup.Navigation, LocalizedText.Pick("Navigation", "导航")),
        new(StandardKeyGroup.Editing, LocalizedText.Pick("Editing", "编辑")),
        new(StandardKeyGroup.Modifiers, LocalizedText.Pick("Modifiers", "修饰键")),
        new(StandardKeyGroup.Function, LocalizedText.Pick("Function", "功能键")),
        new(StandardKeyGroup.Letters, LocalizedText.Pick("Letters", "字母")),
        new(StandardKeyGroup.Numbers, LocalizedText.Pick("Numbers", "数字")),
        new(StandardKeyGroup.Symbols, LocalizedText.Pick("Symbols", "符号")),
        new(StandardKeyGroup.Numpad, LocalizedText.Pick("Numpad", "数字小键盘")),
        new(StandardKeyGroup.Browser, LocalizedText.Pick("Browser", "浏览器"))
    ];

    public static IReadOnlyList<StandardKeyOption> All { get; } =
    [
        new("Escape", "Esc", 0x1B, StandardKeyGroup.Navigation),
        new("Left", LocalizedText.Pick("Left Arrow", "左方向键"), 0x25, StandardKeyGroup.Navigation),
        new("Up", LocalizedText.Pick("Up Arrow", "上方向键"), 0x26, StandardKeyGroup.Navigation),
        new("Right", LocalizedText.Pick("Right Arrow", "右方向键"), 0x27, StandardKeyGroup.Navigation),
        new("Down", LocalizedText.Pick("Down Arrow", "下方向键"), 0x28, StandardKeyGroup.Navigation),
        new("Apps", LocalizedText.Pick("Menu", "菜单"), 0x5D, StandardKeyGroup.Navigation),
        new("ScrollLock", LocalizedText.Pick("Scroll Lock", "滚动锁定"), 0x91, StandardKeyGroup.Navigation),
        new("Pause", LocalizedText.Pick("Pause", "暂停"), 0x13, StandardKeyGroup.Navigation),

        new("Tab", "Tab", 0x09, StandardKeyGroup.Editing),
        new("Enter", "Enter", 0x0D, StandardKeyGroup.Editing),
        new("Space", LocalizedText.Pick("Space", "空格"), 0x20, StandardKeyGroup.Editing),
        new("Backspace", LocalizedText.Pick("Backspace", "退格"), 0x08, StandardKeyGroup.Editing),
        new("Delete", LocalizedText.Pick("Delete", "删除"), 0x2E, StandardKeyGroup.Editing),
        new("Insert", LocalizedText.Pick("Insert", "插入"), 0x2D, StandardKeyGroup.Editing),
        new("Home", "Home", 0x24, StandardKeyGroup.Editing),
        new("End", "End", 0x23, StandardKeyGroup.Editing),
        new("PageUp", "Page Up", 0x21, StandardKeyGroup.Editing),
        new("PageDown", "Page Down", 0x22, StandardKeyGroup.Editing),
        new("CapsLock", LocalizedText.Pick("Caps Lock", "大写锁定"), 0x14, StandardKeyGroup.Editing),
        new("NumLock", LocalizedText.Pick("Num Lock", "数字锁定"), 0x90, StandardKeyGroup.Editing),

        new("Shift", "Shift", 0x10, StandardKeyGroup.Modifiers),
        new("Control", "Ctrl", 0x11, StandardKeyGroup.Modifiers),
        new("Alt", "Alt", 0x12, StandardKeyGroup.Modifiers),
        new("LeftWindows", LocalizedText.Pick("Left Windows", "左 Windows"), 0x5B, StandardKeyGroup.Modifiers),
        new("RightWindows", LocalizedText.Pick("Right Windows", "右 Windows"), 0x5C, StandardKeyGroup.Modifiers),

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

        new("NumPad0", LocalizedText.Pick("NumPad 0", "小键盘 0"), 0x60, StandardKeyGroup.Numpad),
        new("NumPad1", LocalizedText.Pick("NumPad 1", "小键盘 1"), 0x61, StandardKeyGroup.Numpad),
        new("NumPad2", LocalizedText.Pick("NumPad 2", "小键盘 2"), 0x62, StandardKeyGroup.Numpad),
        new("NumPad3", LocalizedText.Pick("NumPad 3", "小键盘 3"), 0x63, StandardKeyGroup.Numpad),
        new("NumPad4", LocalizedText.Pick("NumPad 4", "小键盘 4"), 0x64, StandardKeyGroup.Numpad),
        new("NumPad5", LocalizedText.Pick("NumPad 5", "小键盘 5"), 0x65, StandardKeyGroup.Numpad),
        new("NumPad6", LocalizedText.Pick("NumPad 6", "小键盘 6"), 0x66, StandardKeyGroup.Numpad),
        new("NumPad7", LocalizedText.Pick("NumPad 7", "小键盘 7"), 0x67, StandardKeyGroup.Numpad),
        new("NumPad8", LocalizedText.Pick("NumPad 8", "小键盘 8"), 0x68, StandardKeyGroup.Numpad),
        new("NumPad9", LocalizedText.Pick("NumPad 9", "小键盘 9"), 0x69, StandardKeyGroup.Numpad),
        new("Multiply", LocalizedText.Pick("NumPad *", "小键盘 *"), 0x6A, StandardKeyGroup.Numpad),
        new("Add", LocalizedText.Pick("NumPad +", "小键盘 +"), 0x6B, StandardKeyGroup.Numpad),
        new("Subtract", LocalizedText.Pick("NumPad -", "小键盘 -"), 0x6D, StandardKeyGroup.Numpad),
        new("Decimal", LocalizedText.Pick("NumPad .", "小键盘 ."), 0x6E, StandardKeyGroup.Numpad),
        new("Divide", LocalizedText.Pick("NumPad /", "小键盘 /"), 0x6F, StandardKeyGroup.Numpad),

        new("BrowserBack", LocalizedText.Pick("Back", "后退"), 0xA6, StandardKeyGroup.Browser),
        new("BrowserForward", LocalizedText.Pick("Forward", "前进"), 0xA7, StandardKeyGroup.Browser),
        new("BrowserRefresh", LocalizedText.Pick("Refresh", "刷新"), 0xA8, StandardKeyGroup.Browser),
        new("BrowserStop", LocalizedText.Pick("Stop", "停止"), 0xA9, StandardKeyGroup.Browser),
        new("BrowserSearch", LocalizedText.Pick("Search", "搜索"), 0xAA, StandardKeyGroup.Browser),
        new("BrowserFavorites", LocalizedText.Pick("Favorites", "收藏夹"), 0xAB, StandardKeyGroup.Browser),
        new("BrowserHome", LocalizedText.Pick("Browser Home", "浏览器主页"), 0xAC, StandardKeyGroup.Browser)
    ];

    public static StandardKeyOption? GetOption(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return All.FirstOrDefault(option => string.Equals(option.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeKey(string? key)
    {
        return GetOption(key)?.Key ?? string.Empty;
    }

    public static string GetLabel(string? key)
    {
        return GetOption(key)?.Label ?? LocalizedText.Pick("Choose standard key", "选择标准按键");
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

    public static bool TryGetVirtualKey(string? key, out byte virtualKey)
    {
        if (GetOption(key) is { VirtualKey: >= byte.MinValue and <= byte.MaxValue } option)
        {
            virtualKey = (byte)option.VirtualKey;
            return true;
        }

        virtualKey = 0;
        return false;
    }
}
