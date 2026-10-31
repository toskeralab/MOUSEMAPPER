using System.Diagnostics;
using MouseMapper.Interop;
using MouseMapper.Models;

namespace MouseMapper.Services;

/// <summary>
/// Reproduz uma automação respeitando os delays gravados. Os delays são calculados
/// contra um cronômetro absoluto para que o erro não se acumule ao longo da macro.
/// </summary>
public sealed class AutomationPlayer
{
    private int _playing;

    public bool IsPlaying => Volatile.Read(ref _playing) == 1;

    public Guid? CurrentAutomationId { get; private set; }

    /// <summary>
    /// Executa todos os eventos da automação. Deve ser chamado de uma thread de fundo
    /// (Task.Run): o método não bloqueia a UI e respeita o <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task PlayAsync(Automation automation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(automation);

        // Garante uma única reprodução por vez.
        if (Interlocked.CompareExchange(ref _playing, 1, 0) != 0)
        {
            return;
        }

        CurrentAutomationId = automation.Id;
        try
        {
            await PlayCoreAsync(automation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CurrentAutomationId = null;
            Volatile.Write(ref _playing, 0);
        }
    }

    private static async Task PlayCoreAsync(Automation automation, CancellationToken cancellationToken)
    {
        var events = automation.Events;
        if (events.Count == 0)
        {
            return;
        }

        FileLogger.Instance.Info($"Reproduzindo '{automation.Name}' ({events.Count} eventos).");
        var clock = Stopwatch.StartNew();

        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long remaining = item.Timestamp - clock.ElapsedMilliseconds;
            if (remaining > 0)
            {
                await Task.Delay((int)Math.Min(int.MaxValue, remaining), cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Apply(item);
        }

        FileLogger.Instance.Info($"Reprodução de '{automation.Name}' concluída.");
    }

    private static void Apply(MouseEventRecord item)
    {
        switch (item.Type)
        {
            case MouseEventType.MouseMove:
                InputSimulator.MoveTo(item.X, item.Y);
                break;

            case MouseEventType.LeftDown:
                InputSimulator.LeftDown(item.X, item.Y);
                break;

            case MouseEventType.LeftUp:
                InputSimulator.LeftUp(item.X, item.Y);
                break;

            case MouseEventType.RightDown:
                InputSimulator.RightDown(item.X, item.Y);
                break;

            case MouseEventType.RightUp:
                InputSimulator.RightUp(item.X, item.Y);
                break;

            case MouseEventType.MiddleDown:
                InputSimulator.MiddleDown(item.X, item.Y);
                break;

            case MouseEventType.MiddleUp:
                InputSimulator.MiddleUp(item.X, item.Y);
                break;

            case MouseEventType.Wheel:
                InputSimulator.Wheel(item.Data, item.X, item.Y);
                break;

            case MouseEventType.DoubleClick:
                InputSimulator.DoubleClick(item.X, item.Y);
                break;
        }
    }

}
