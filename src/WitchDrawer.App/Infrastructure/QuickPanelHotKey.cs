using System.Globalization;
using System.Windows.Input;
using WitchDrawer.Native.HotKeys;

namespace WitchDrawer.App.Infrastructure;

internal sealed record QuickPanelHotKey(HotKeyModifiers Modifiers, uint VirtualKey)
{
    private const HotKeyModifiers PersistedModifiers =
        HotKeyModifiers.Alt
        | HotKeyModifiers.Control
        | HotKeyModifiers.Shift
        | HotKeyModifiers.Win;

    public static QuickPanelHotKey Default { get; } =
        new(HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x57);

    public HotKeyModifiers RegistrationModifiers => Modifiers | HotKeyModifiers.NoRepeat;

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotKeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Win))
            {
                parts.Add("Win");
            }

            parts.Add(FormatKey(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public bool IsValid =>
        VirtualKey is > 0 and <= 0xFE
        && (Modifiers & ~PersistedModifiers) == 0
        && (Modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win)) != 0;

    public string Serialize()
    {
        return $"{(uint)Modifiers:X}:{VirtualKey:X}";
    }

    public static bool TryParse(string? value, out QuickPanelHotKey hotKey)
    {
        hotKey = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var modifiers)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var virtualKey))
        {
            return false;
        }

        var candidate = new QuickPanelHotKey((HotKeyModifiers)modifiers, virtualKey);
        if (!candidate.IsValid)
        {
            return false;
        }

        hotKey = candidate;
        return true;
    }

    private static string FormatKey(uint virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "Num " + ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Next => "Page Down",
            Key.Prior => "Page Up",
            _ => key.ToString()
        };
    }
}
