using System.Text.Json.Serialization;

namespace MouseMapper.Models;

/// <summary>
/// Tipos de evento de mouse gravados. O gravador salva primitivas (down/up/move/wheel)
/// e o pós-processamento converte pares de cliques em <see cref="DoubleClick"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MouseEventType
{
    MouseMove,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel,
    DoubleClick
}
