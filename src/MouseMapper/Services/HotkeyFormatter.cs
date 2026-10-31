using MouseMapper.Interop;

namespace MouseMapper.Services;

/// <summary>Converte teclas virtuais em texto legível e lê o estado atual dos modificadores.</summary>
internal static class HotkeyFormatter
{
    public const uint ModAlt = NativeMethods.MOD_ALT;
    public const uint ModControl = NativeMethods.MOD_CONTROL;
    public const uint ModShift = NativeMethods.MOD_SHIFT;
    public const uint ModWin = NativeMethods.MOD_WIN;

    /// <summary>Lê Ctrl/Shift/Alt/Win fisicamente pressionados neste instante.</summary>
    public static uint ReadCurrentModifiers()
    {
        uint modifiers = 0;

        if (IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_LCONTROL) || IsDown(NativeMethods.VK_RCONTROL))
        {
            modifiers |= ModControl;
        }

        if (IsDown(NativeMethods.VK_SHIFT) || IsDown(NativeMethods.VK_LSHIFT) || IsDown(NativeMethods.VK_RSHIFT))
        {
            modifiers |= ModShift;
        }

        if (IsDown(NativeMethods.VK_MENU) || IsDown(NativeMethods.VK_LMENU) || IsDown(NativeMethods.VK_RMENU))
        {
            modifiers |= ModAlt;
        }

        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN))
        {
            modifiers |= ModWin;
        }

        return modifiers;
    }

    public static bool IsModifierKey(uint virtualKey) => virtualKey switch
    {
        NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU => true,
        NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT => true,
        NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL => true,
        NativeMethods.VK_LMENU or NativeMethods.VK_RMENU => true,
        NativeMethods.VK_LWIN or NativeMethods.VK_RWIN => true,
        _ => false
    };

    /// <summary>
    /// Atalhos sem modificador só são aceitos para teclas de função e teclas de sistema,
    /// evitando que uma letra comum passe a ser capturada globalmente.
    /// </summary>
    public static bool IsAllowedStandalone(uint virtualKey)
        => (virtualKey >= 0x70 && virtualKey <= 0x87) // F1..F24
           || virtualKey == 0x2C                      // PrintScreen
           || virtualKey == 0x13                      // Pause
           || virtualKey == 0x91                      // ScrollLock
           || (virtualKey >= 0xA6 && virtualKey <= 0xB7); // teclas de mídia

    /// <summary>Monta o texto do atalho. VirtualKey 0 significa "apenas modificadores".</summary>
    public static string Describe(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>(5);

        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        if (virtualKey != 0)
        {
            parts.Add(DescribeKey(virtualKey));
        }

        return string.Join("+", parts);
    }

    public static string DescribeKey(uint virtualKey) => virtualKey switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x14 => "CapsLock",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x5B or 0x5C => "Win",
        0x5D => "Menu",
        0x6A => "Num*",
        0x6B => "Num+",
        0x6C => "NumEnter",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        0x90 => "NumLock",
        0x91 => "ScrollLock",
        0xA0 => "LShift",
        0xA1 => "RShift",
        0xA2 => "LCtrl",
        0xA3 => "RCtrl",
        0xA4 => "LAlt",
        0xA5 => "RAlt",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (virtualKey - 0x60),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "'",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "~",
        _ => $"VK{virtualKey:X2}"
    };

    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
