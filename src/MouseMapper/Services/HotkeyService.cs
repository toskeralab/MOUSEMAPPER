using System.Runtime.InteropServices;
using System.Windows.Interop;
using MouseMapper.Interop;
using MouseMapper.Models;

namespace MouseMapper.Services;

/// <summary>
/// Registra atalhos globais via RegisterHotKey usando uma janela invisível
/// (message-only window) que recebe WM_HOTKEY.
///
/// Por que RegisterHotKey e não apenas o hook de teclado?
/// - RegisterHotKey devolve ERROR_HOTKEY_ALREADY_REGISTERED (1409) quando o atalho
///   já pertence a outro aplicativo — é exatamente a regra de conflito do produto;
/// - a combinação fica reservada para o MOUSEMAPPER, evitando disparo duplicado
///   (hook + hotkey) e impedindo que o app em foco também reaja à tecla.
/// O hook WH_KEYBOARD_LL fica responsável pela captura/preview do atalho.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int ValidationId = 0x5F5F;
    private const int FirstAutomationId = 0x1000;

    private readonly object _gate = new();
    private readonly HwndSource _messageWindow;
    private readonly Dictionary<int, Guid> _activeByNativeId = new();
    private readonly Dictionary<Guid, int> _nativeIdByAutomation = new();
    private bool _suspended;
    private int _nextId = FirstAutomationId;
    private bool _disposed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("MouseMapperHotkeySink")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE: janela invisível, sem interferir na UI
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    /// <summary>Disparado quando um atalho registrado é pressionado.</summary>
    public event Action<Guid>? HotkeyPressed;

    public bool IsSuspended
    {
        get
        {
            lock (_gate)
            {
                return _suspended;
            }
        }
    }

    /// <summary>
    /// Testa uma combinação sem deixá-la registrada: replica a regra do Windows
    /// (1409) e também verifica duplicidade dentro do próprio MOUSEMAPPER.
    /// </summary>
    public bool TryValidate(
        HotkeyDefinition hotkey,
        IEnumerable<Automation> automations,
        Guid? ignoreAutomationId,
        out string error)
    {
        lock (_gate)
        {
            if (!hotkey.IsValid)
            {
                error = "Combinação inválida.";
                return false;
            }

            bool ignoredComboIsRegistered = false;

            foreach (var automation in automations)
            {
                if (automation.Id == ignoreAutomationId)
                {
                    ignoredComboIsRegistered = _nativeIdByAutomation.ContainsKey(automation.Id)
                                               && automation.Hotkey.HasSameCombo(hotkey);
                    continue;
                }

                if (automation.Hotkey.HasSameCombo(hotkey))
                {
                    error = $"Atalho em uso pela automação \"{automation.Name}\". Escolha outro.";
                    return false;
                }
            }

            if (ignoredComboIsRegistered)
            {
                // A própria automação editada já reservou essa combinação.
                error = string.Empty;
                return true;
            }

            if (!NativeMethods.RegisterHotKey(_messageWindow.Handle, ValidationId, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "Atalho em uso, escolha outro."
                    : $"Não foi possível validar o atalho (código {code}).";
                return false;
            }

            NativeMethods.UnregisterHotKey(_messageWindow.Handle, ValidationId);
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Registra o atalho de uma automação. Retorna false e detalha o erro.</summary>
    public bool TryRegister(Automation automation, out string error)
    {
        lock (_gate)
        {
            error = string.Empty;

            if (_suspended)
            {
                return true; // será registrado ao retomar (Resume)
            }

            if (_nativeIdByAutomation.ContainsKey(automation.Id))
            {
                return true;
            }

            if (!RegisterCore(automation, out error))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>Registra todas as automações válidas e devolve as que falharam.</summary>
    public IReadOnlyDictionary<Guid, string> RegisterAll(IEnumerable<Automation> automations)
    {
        lock (_gate)
        {
            var failures = new Dictionary<Guid, string>();
            var usedCombos = new HashSet<(uint Modifiers, uint VirtualKey)>();
            if (_suspended)
            {
                return failures;
            }

            foreach (var automation in automations)
            {
                if (!automation.Hotkey.IsValid)
                {
                    failures[automation.Id] = "Atalho inválido ou não definido.";
                    continue;
                }

                var combo = (automation.Hotkey.Modifiers, automation.Hotkey.VirtualKey);
                if (!usedCombos.Add(combo))
                {
                    failures[automation.Id] = $"O atalho {automation.Hotkey.Display} está duplicado com outra automação.";
                    continue;
                }

                if (!_nativeIdByAutomation.ContainsKey(automation.Id) && !RegisterCore(automation, out string error))
                {
                    failures[automation.Id] = error;
                }
            }

            return failures;
        }
    }

    public bool IsRegistered(Guid automationId)
    {
        lock (_gate)
        {
            return _nativeIdByAutomation.ContainsKey(automationId);
        }
    }

    public void Unregister(Guid automationId)
    {
        lock (_gate)
        {
            if (_nativeIdByAutomation.TryGetValue(automationId, out int nativeId))
            {
                NativeMethods.UnregisterHotKey(_messageWindow.Handle, nativeId);
                _nativeIdByAutomation.Remove(automationId);
                _activeByNativeId.Remove(nativeId);
            }
        }
    }

    /// <summary>
    /// Remove todos os atalhos temporariamente. Usado enquanto o usuário edita ou
    /// captura uma combinação, para que os atalhos existentes não disparem no meio.
    /// </summary>
    public void Suspend()
    {
        lock (_gate)
        {
            if (_suspended)
            {
                return;
            }

            _suspended = true;
            foreach (int nativeId in _activeByNativeId.Keys.ToList())
            {
                NativeMethods.UnregisterHotKey(_messageWindow.Handle, nativeId);
            }

            _activeByNativeId.Clear();
            _nativeIdByAutomation.Clear();
        }
    }

    /// <summary>Reativa todos os atalhos após uma suspensão.</summary>
    public IReadOnlyDictionary<Guid, string> Resume(IEnumerable<Automation> automations)
    {
        lock (_gate)
        {
            _suspended = false;
        }

        return RegisterAll(automations);
    }

    private bool RegisterCore(Automation automation, out string error)
    {
        error = string.Empty;

        int nativeId = _nextId++;
        if (!NativeMethods.RegisterHotKey(
                _messageWindow.Handle,
                nativeId,
                automation.Hotkey.Modifiers | NativeMethods.MOD_NOREPEAT,
                automation.Hotkey.VirtualKey))
        {
            int code = Marshal.GetLastWin32Error();
            error = code == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"O atalho {automation.Hotkey.Display} já está em uso por outro aplicativo."
                : $"Falha ao registrar {automation.Hotkey.Display} (código {code}).";
            FileLogger.Instance.Warn(error);
            return false;
        }

        _activeByNativeId[nativeId] = automation.Id;
        _nativeIdByAutomation[automation.Id] = nativeId;
        FileLogger.Instance.Info($"Atalho {automation.Hotkey.Display} registrado para '{automation.Name}'.");
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        Guid automationId;
        lock (_gate)
        {
            _activeByNativeId.TryGetValue(wParam.ToInt32(), out automationId);
        }

        if (automationId == Guid.Empty)
        {
            return IntPtr.Zero;
        }

        handled = true;
        try
        {
            HotkeyPressed?.Invoke(automationId);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao tratar o atalho pressionado.", ex);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            lock (_gate)
            {
                foreach (int nativeId in _activeByNativeId.Keys.ToList())
                {
                    NativeMethods.UnregisterHotKey(_messageWindow.Handle, nativeId);
                }

                NativeMethods.UnregisterHotKey(_messageWindow.Handle, ValidationId);
                _activeByNativeId.Clear();
                _nativeIdByAutomation.Clear();
            }

            _messageWindow.RemoveHook(WndProc);
            _messageWindow.Dispose();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao liberar os atalhos globais.", ex);
        }

        GC.SuppressFinalize(this);
    }
}
