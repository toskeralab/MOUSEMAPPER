namespace MouseMapper.Models;

/// <summary>Envelope persistido em automations.json (ao lado do executável).</summary>
public sealed class AutomationFile
{
    public int Version { get; set; } = 1;

    public List<Automation> Automations { get; set; } = new();
}
