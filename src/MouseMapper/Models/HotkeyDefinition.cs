namespace MouseMapper.Models;

/// <summary>
/// Combinação de teclas registrada globalmente (RegisterHotKey).
/// Guardamos o texto legível e também os códigos nativos para não depender de parsing.
/// </summary>
public sealed class HotkeyDefinition
{
    /// <summary>Texto exibido na UI, por exemplo "Ctrl+Shift+1".</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>Flags MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN.</summary>
    public uint Modifiers { get; set; }

    /// <summary>Código virtual da tecla principal (VK_*).</summary>
    public uint VirtualKey { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => VirtualKey != 0 && !string.IsNullOrWhiteSpace(Display);

    public bool HasSameCombo(HotkeyDefinition? other)
        => other is not null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public HotkeyDefinition Clone() => new()
    {
        Display = Display,
        Modifiers = Modifiers,
        VirtualKey = VirtualKey
    };

    public override string ToString() => Display;
}
