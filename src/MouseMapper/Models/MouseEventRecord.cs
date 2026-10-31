namespace MouseMapper.Models;

/// <summary>
/// Um passo da macro. <see cref="Timestamp"/> é o tempo em milissegundos desde o
/// início da gravação (o primeiro evento sempre começa em 0), o que torna o cálculo
/// de delays entre eventos uma simples subtração.
/// </summary>
public sealed class MouseEventRecord
{
    public MouseEventType Type { get; set; }

    /// <summary>Coordenada X absoluta em pixels da área de trabalho virtual.</summary>
    public int X { get; set; }

    /// <summary>Coordenada Y absoluta em pixels da área de trabalho virtual.</summary>
    public int Y { get; set; }

    /// <summary>Tempo em ms desde o início da gravação.</summary>
    public long Timestamp { get; set; }

    /// <summary>Delta do scroll (múltiplos de 120) quando <see cref="Type"/> é Wheel.</summary>
    public int Data { get; set; }

    public MouseEventRecord Clone() => new()
    {
        Type = Type,
        X = X,
        Y = Y,
        Timestamp = Timestamp,
        Data = Data
    };
}
