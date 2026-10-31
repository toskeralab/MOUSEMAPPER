using System.Text.Json.Serialization;

namespace MouseMapper.Models;

/// <summary>Uma automação (macro) completa: nome, atalho, data e sequência de eventos.</summary>
public sealed class Automation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public HotkeyDefinition Hotkey { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MouseEventRecord> Events { get; set; } = new();

    [JsonIgnore]
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string EventSummary => Events.Count == 0
        ? "sem gravação"
        : $"{Events.Count} evento{(Events.Count == 1 ? "" : "s")}";

    [JsonIgnore]
    public string Tooltip => $"{Name}\nAtalho: {Hotkey.Display}\n{EventSummary}\nCriada em {CreatedAtDisplay}";
}
