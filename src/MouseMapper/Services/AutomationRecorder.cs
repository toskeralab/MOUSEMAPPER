using System.Diagnostics;
using MouseMapper.Interop;
using MouseMapper.Models;

namespace MouseMapper.Services;

/// <summary>
/// Grava a sequência de eventos de mouse em uma thread com bomba de mensagens
/// (o hook de baixo nível é chamado nessa mesma thread; por isso ele é instalado
/// a partir da UI e os eventos são apenas enfileirados na lista).
/// </summary>
public sealed class AutomationRecorder : IDisposable
{
    private const int MaxEvents = 200_000;      // trava de segurança (arquivo JSON gigante)
    private const int MoveThrottleMs = 8;        // no máximo ~125 movimentos por segundo

    private readonly MouseHook _hook = new();
    private readonly List<MouseEventRecord> _events = new();
    private readonly Stopwatch _clock = new();

    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private long _lastMoveAt = -1000;
    private int _lastMoveX = int.MinValue;
    private int _lastMoveY = int.MinValue;
    private bool _disposed;

    public AutomationRecorder()
    {
        _hook.MouseEvent += OnMouseEvent;
    }

    public bool IsRecording { get; private set; }

    public int EventCount => _events.Count;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRecording)
        {
            return;
        }

        _events.Clear();
        _leftDown = _rightDown = _middleDown = false;
        _lastMoveAt = -1000;
        _lastMoveX = _lastMoveY = int.MinValue;
        _clock.Restart();

        _hook.Start();
        IsRecording = true;
        FileLogger.Instance.Info("Gravação iniciada.");
    }

    /// <summary>
    /// Encerra a captura e devolve os eventos normalizados (tempo relativo ao primeiro
    /// evento, duplo clique consolidado). É seguro chamar quando não está gravando.
    /// </summary>
    public List<MouseEventRecord> Stop()
    {
        if (IsRecording)
        {
            IsRecording = false;
            _hook.Stop();
            _clock.Stop();
            FileLogger.Instance.Info($"Gravação finalizada com {_events.Count} evento(s) brutos.");
        }

        var snapshot = _events.Select(e => e.Clone()).ToList();
        return Normalize(snapshot);
    }

    private void OnMouseEvent(object? sender, MouseHookEventArgs e)
    {
        if (!IsRecording || _events.Count >= MaxEvents)
        {
            return;
        }

        long now = _clock.ElapsedMilliseconds;

        switch (e.Message)
        {
            case NativeMethods.WM_MOUSEMOVE:
                if (_leftDown || _rightDown || _middleDown)
                {
                    AddMove(now, e.X, e.Y);
                }

                break;

            case NativeMethods.WM_LBUTTONDOWN:
            case NativeMethods.WM_LBUTTONDBLCLK:
                // Alguns drivers entregam DBLCLK e DOWN; o guarda evita down duplicado.
                _leftDown = true;
                AddButton(now, MouseEventType.LeftDown, e.X, e.Y);
                break;

            case NativeMethods.WM_LBUTTONUP:
                _leftDown = false;
                AddButton(now, MouseEventType.LeftUp, e.X, e.Y);
                break;

            case NativeMethods.WM_RBUTTONDOWN:
            case NativeMethods.WM_RBUTTONDBLCLK:
                _rightDown = true;
                AddButton(now, MouseEventType.RightDown, e.X, e.Y);
                break;

            case NativeMethods.WM_RBUTTONUP:
                _rightDown = false;
                AddButton(now, MouseEventType.RightUp, e.X, e.Y);
                break;

            case NativeMethods.WM_MBUTTONDOWN:
                _middleDown = true;
                AddButton(now, MouseEventType.MiddleDown, e.X, e.Y);
                break;

            case NativeMethods.WM_MBUTTONUP:
                _middleDown = false;
                AddButton(now, MouseEventType.MiddleUp, e.X, e.Y);
                break;

            case NativeMethods.WM_MOUSEWHEEL:
            {
                int delta = (short)((e.MouseData >> 16) & 0xFFFF);
                _events.Add(new MouseEventRecord
                {
                    Type = MouseEventType.Wheel,
                    X = e.X,
                    Y = e.Y,
                    Timestamp = now,
                    Data = delta
                });
                break;
            }
        }
    }

    private void AddButton(long now, MouseEventType type, int x, int y)
    {
        // Evita down/up repetido (mensagens duplicadas do mesmo botão).
        if (_events.Count > 0)
        {
            var last = _events[^1];
            if (last.Type == type && last.X == x && last.Y == y)
            {
                return;
            }
        }

        _events.Add(new MouseEventRecord { Type = type, X = x, Y = y, Timestamp = now });

        if (type == MouseEventType.LeftUp || type == MouseEventType.RightUp || type == MouseEventType.MiddleUp)
        {
            _lastMoveAt = -1000;
        }
    }

    private void AddMove(long now, int x, int y)
    {
        if (now - _lastMoveAt < MoveThrottleMs)
        {
            return;
        }

        if (Math.Abs(x - _lastMoveX) < 2 && Math.Abs(y - _lastMoveY) < 2)
        {
            return;
        }

        _lastMoveAt = now;
        _lastMoveX = x;
        _lastMoveY = y;
        _events.Add(new MouseEventRecord { Type = MouseEventType.MouseMove, X = x, Y = y, Timestamp = now });
    }

    /// <summary>
    /// Consolida os eventos: reamostra o tempo para começar em 0 e transforma
    /// down/up + down/up próximos em um único <see cref="MouseEventType.DoubleClick"/>.
    /// </summary>
    public static List<MouseEventRecord> Normalize(List<MouseEventRecord> raw)
    {
        var result = new List<MouseEventRecord>(raw.Count);
        if (raw.Count == 0)
        {
            return result;
        }

        long offset = raw[0].Timestamp;
        foreach (var item in raw)
        {
            item.Timestamp = Math.Max(0, item.Timestamp - offset);
        }

        int doubleClickWindow = (int)NativeMethods.GetDoubleClickTime();

        for (int i = 0; i < raw.Count; i++)
        {
            if (i + 3 < raw.Count
                && raw[i].Type == MouseEventType.LeftDown
                && raw[i + 1].Type == MouseEventType.LeftUp
                && raw[i + 2].Type == MouseEventType.LeftDown
                && raw[i + 3].Type == MouseEventType.LeftUp
                && raw[i + 2].Timestamp - raw[i + 1].Timestamp <= doubleClickWindow
                && Math.Abs(raw[i + 2].X - raw[i].X) <= 4
                && Math.Abs(raw[i + 2].Y - raw[i].Y) <= 4)
            {
                result.Add(new MouseEventRecord
                {
                    Type = MouseEventType.DoubleClick,
                    X = raw[i].X,
                    Y = raw[i].Y,
                    Timestamp = raw[i].Timestamp
                });
                i += 3;
                continue;
            }

            result.Add(raw[i]);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hook.MouseEvent -= OnMouseEvent;
        _hook.Dispose();
        _events.Clear();
        GC.SuppressFinalize(this);
    }
}
