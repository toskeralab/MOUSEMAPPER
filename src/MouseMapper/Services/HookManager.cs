using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseMapper.Interop;

namespace MouseMapper.Services;

/// <summary>Evento bruto do hook de mouse (coordenadas em pixels de tela).</summary>
public sealed class MouseHookEventArgs : EventArgs
{
    public MouseHookEventArgs(int message, int x, int y, int mouseData)
    {
        Message = message;
        X = x;
        Y = y;
        MouseData = mouseData;
    }

    public int Message { get; }

    public int X { get; }

    public int Y { get; }

    /// <summary>Delta do scroll em <see cref="NativeMethods.WM_MOUSEWHEEL"/>.</summary>
    public int MouseData { get; }
}

/// <summary>Evento bruto do hook de teclado.</summary>
public sealed class KeyboardHookEventArgs : EventArgs
{
    public KeyboardHookEventArgs(int message, int virtualKey, bool isKeyDown)
    {
        Message = message;
        VirtualKey = virtualKey;
        IsKeyDown = isKeyDown;
    }

    public int Message { get; }

    public int VirtualKey { get; }

    public bool IsKeyDown { get; }
}

/// <summary>
/// Base dos hooks de baixo nível (WH_MOUSE_LL / WH_KEYBOARD_LL).
/// Mantém o delegate vivo (evita GC do callback), converte exceções em log e garante
/// que UnhookWindowsHookEx seja sempre chamado.
/// </summary>
public abstract class LowLevelHook : IDisposable
{
    private readonly int _hookType;
    private readonly string _name;
    private NativeMethods.LowLevelProc? _callback; // campo: mantém o delegate vivo
    private IntPtr _handle;
    private bool _disposed;

    protected LowLevelHook(int hookType, string name)
    {
        _hookType = hookType;
        _name = name;
    }

    public bool IsRunning => _handle != IntPtr.Zero;

    /// <summary>Instala o hook. Lança <see cref="Win32Exception"/> se a API falhar.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            return;
        }

        _callback = HookCallback;
        IntPtr module = NativeMethods.GetModuleHandle(null);
        _handle = NativeMethods.SetWindowsHookEx(_hookType, _callback, module, 0);

        if (_handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            _callback = null;
            throw new Win32Exception(error, $"Não foi possível instalar o hook {_name} (código {error}).");
        }

        FileLogger.Instance.Info($"Hook {_name} instalado.");
    }

    /// <summary>Remove o hook. Seguro para chamar mais de uma vez.</summary>
    public void Stop()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
            FileLogger.Instance.Info($"Hook {_name} removido.");
        }

        _callback = null;
    }

    protected abstract IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Deixa o evento seguir para os demais hooks/aplicativos.</summary>
    protected IntPtr CallNext(int nCode, IntPtr wParam, IntPtr lParam)
        => NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                return OnHook(nCode, wParam, lParam);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error($"Erro ao processar evento do hook {_name}.", ex);
            }
        }

        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Hook global de mouse: captura cliques, movimentos e scroll.</summary>
public sealed class MouseHook : LowLevelHook
{
    public MouseHook() : base(NativeMethods.WH_MOUSE_LL, "WH_MOUSE_LL")
    {
    }

    public event EventHandler<MouseHookEventArgs>? MouseEvent;

    protected override IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        MouseEvent?.Invoke(this, new MouseHookEventArgs((int)wParam, data.pt.X, data.pt.Y, (int)data.mouseData));
        return CallNext(nCode, wParam, lParam);
    }
}

/// <summary>
/// Hook global de teclado. Usado na tela de captura do atalho: permite visualizar as
/// teclas em tempo real e bloquear o repasse delas enquanto o usuário define a combinação.
/// </summary>
public sealed class KeyboardHook : LowLevelHook
{
    public KeyboardHook() : base(NativeMethods.WH_KEYBOARD_LL, "WH_KEYBOARD_LL")
    {
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    /// <summary>Quando true, as teclas não chegam aos demais aplicativos (retorna 1).</summary>
    public bool Suppress { get; set; }

    protected override IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int message = (int)wParam;
        bool isKeyDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

        if (isKeyDown || message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            KeyEvent?.Invoke(this, new KeyboardHookEventArgs(message, (int)data.vkCode, isKeyDown));

            if (Suppress)
            {
                return new IntPtr(1);
            }
        }

        return CallNext(nCode, wParam, lParam);
    }
}
