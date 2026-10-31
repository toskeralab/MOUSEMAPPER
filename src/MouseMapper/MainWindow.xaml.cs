using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MouseMapper.Interop;
using MouseMapper.Models;
using MouseMapper.Services;
using MouseMapper.Views;
using WinForms = System.Windows.Forms;

namespace MouseMapper;

/// <summary>
/// Janela principal e orquestradora do app: mantém a coleção de automações,
/// coordena gravação/reprodução, atalhos globais e o ícone da bandeja.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush SuccessBrush = CreateBrush(0x3D, 0xD6, 0x8C);
    private static readonly Brush DangerBrush = CreateBrush(0xFF, 0x5D, 0x6C);
    private static readonly Brush AccentBrush = CreateBrush(0x4C, 0x8D, 0xFF);
    private static readonly Brush MutedBrush = CreateBrush(0x8C, 0x95, 0xAB);

    private readonly FileLogger _log = FileLogger.Instance;
    private readonly AutomationStore _store = new();
    private readonly ObservableCollection<Automation> _automations = new();
    private readonly HotkeyService _hotkeys;
    private readonly AutomationPlayer _player = new();
    private readonly TrayIconService _tray;

    private AutomationRecorder? _recorder;
    private Guid? _recordingAutomationId;
    private CancellationTokenSource? _playbackCts;
    private Guid? _editingAutomationId;
    private HotkeyDefinition? _pendingHotkey;
    private bool _hotkeyValid;
    private bool _isExiting;
    private bool _trayHintShown;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        AutomationsGrid.ItemsSource = _automations;

        _tray = new TrayIconService(LoadTrayIcon());
        _tray.OpenRequested += () => RunOnUiThread(ShowFromTray);
        _tray.ExitRequested += () => RunOnUiThread(ExitApplication);
        _tray.StopRecordingRequested += () => RunOnUiThread(() => _ = StopRecordingAsync());
        _tray.CancelPlaybackRequested += () => RunOnUiThread(CancelPlayback);
    }

    // ==================================================================
    // Ciclo de vida
    // ==================================================================
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var loaded = _store.Load(out string? warning);
        foreach (var automation in loaded)
        {
            _automations.Add(automation);
        }

        RefreshList();
        UpdateTrayState();

        var failures = _hotkeys.RegisterAll(_automations);
        ReportRegistrationFailures(failures, showDialog: true);

        if (warning is not null)
        {
            MessageBox.Show(this, warning, "Arquivo de automações", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SetStatus(_automations.Count == 0
            ? "Pronto. Clique em “Mapear nova automação” para começar."
            : $"Pronto. {_automations.Count} automação(ões) carregada(s).");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        TryApplyDarkTitleBar();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // Minimizar também manda o app para a bandeja.
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showHint: true);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        // O "X" apenas esconde a janela; o app continua ativo na bandeja.
        e.Cancel = true;
        HideToTray(showHint: true);
    }

    // ==================================================================
    // Editor (nome + atalho)
    // ==================================================================
    private void NewAutomation_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditAutomation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Automation automation)
        {
            OpenEditor(automation.Id);
        }
    }

    private void OpenEditor(Guid? automationId, string? presetName = null, HotkeyDefinition? presetHotkey = null, string? hotkeyError = null)
    {
        if (_recorder is { IsRecording: true })
        {
            MessageBox.Show(this, "Pare a gravação atual antes de editar outra automação.", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_player.IsPlaying)
        {
            MessageBox.Show(this, "Aguarde o fim da reprodução antes de editar as automações.", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Suspende os atalhos existentes para não dispararem enquanto o usuário
        // pressiona a combinação desejada (e para liberar a validação via RegisterHotKey).
        _hotkeys.Suspend();

        _editingAutomationId = automationId;
        NameMessage.Visibility = Visibility.Collapsed;

        if (automationId is Guid id && Find(id) is { } automation)
        {
            EditorTitle.Text = "Editar automação";
            EditorSubtitle.Text = "Ajuste nome e atalho, salve, ou regrave a sequência de eventos.";
            RecordButton.Content = "Salvar";
            RecordAgainButton.Visibility = Visibility.Visible;
            NameBox.Text = presetName ?? automation.Name;

            var hotkey = presetHotkey ?? automation.Hotkey;
            if (hotkeyError is not null)
            {
                SetHotkeyState(hotkey, hotkeyError, isValid: false);
            }
            else
            {
                ValidateAndSetHotkey(hotkey);
            }
        }
        else
        {
            EditorTitle.Text = "Nova automação";
            EditorSubtitle.Text = "Defina um nome e uma combinação de teclas para gravar sua macro.";
            RecordButton.Content = "Gravar automação";
            RecordAgainButton.Visibility = Visibility.Collapsed;
            NameBox.Text = presetName ?? string.Empty;

            if (presetHotkey is not null)
            {
                if (hotkeyError is not null)
                {
                    SetHotkeyState(presetHotkey, hotkeyError, isValid: false);
                }
                else
                {
                    ValidateAndSetHotkey(presetHotkey);
                }
            }
            else
            {
                SetHotkeyState(null, string.Empty, isValid: false);
            }
        }

        EditorOverlay.Visibility = Visibility.Visible;
        UpdateRecordButtonState();
        NameBox.Focus();
        NameBox.CaretIndex = NameBox.Text.Length;
    }

    private void CloseEditor()
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        _editingAutomationId = null;
        _pendingHotkey = null;
        _hotkeyValid = false;
        NameMessage.Visibility = Visibility.Collapsed;
    }

    private void CancelEditor_Click(object sender, RoutedEventArgs e)
    {
        CloseEditor();
        var failures = _hotkeys.Resume(_automations);
        ReportRegistrationFailures(failures, showDialog: false);
        UpdateTrayState();
        SetStatus("Edição cancelada.");
    }

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NameMessage.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameMessage.Visibility = Visibility.Collapsed;
        }

        UpdateRecordButtonState();
    }

    private void UpdateRecordButtonState()
    {
        RecordButton.IsEnabled = _hotkeyValid && !string.IsNullOrWhiteSpace(NameBox.Text);
    }

    private void HotkeyBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (!_hotkeys.IsSuspended)
            {
                _hotkeys.Suspend();
            }

            var dialog = new HotkeyCaptureWindow
            {
                Owner = IsVisible ? this : null
            };

            if (dialog.ShowDialog() == true && dialog.Result is { } hotkey)
            {
                ValidateAndSetHotkey(hotkey);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao capturar o atalho.", ex);
            MessageBox.Show(this, $"Não foi possível capturar o atalho.\n\n{ex.Message}", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ValidateAndSetHotkey(HotkeyDefinition hotkey)
    {
        if (!_hotkeys.TryValidate(hotkey, _automations, _editingAutomationId, out string error))
        {
            SetHotkeyState(hotkey, string.IsNullOrWhiteSpace(error) ? "Atalho em uso, escolha outro." : error, isValid: false);
            return;
        }

        SetHotkeyState(hotkey, "Atalho disponível — pronto para gravar.", isValid: true);
    }

    private void SetHotkeyState(HotkeyDefinition? hotkey, string message, bool isValid)
    {
        _pendingHotkey = hotkey;
        _hotkeyValid = isValid && hotkey is { IsValid: true };

        HotkeyPreview.Text = hotkey is null || string.IsNullOrWhiteSpace(hotkey.Display)
            ? "Clique para capturar o atalho…"
            : hotkey.Display;

        HotkeyPreview.Foreground = isValid ? SuccessBrush : hotkey is null ? MutedBrush : DangerBrush;
        HotkeyBox.BorderBrush = isValid ? SuccessBrush : hotkey is null ? MutedBrush : DangerBrush;

        HotkeyMessage.Text = message;
        HotkeyMessage.Foreground = isValid ? SuccessBrush : DangerBrush;
        HotkeyMessage.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        UpdateRecordButtonState();
    }

    private bool TryReadEditor(out string name, out HotkeyDefinition hotkey)
    {
        name = NameBox.Text.Trim();
        hotkey = _pendingHotkey ?? new HotkeyDefinition();

        if (name.Length == 0)
        {
            NameMessage.Text = "Informe um nome para a automação.";
            NameMessage.Visibility = Visibility.Visible;
            NameBox.Focus();
            return false;
        }

        if (!_hotkeyValid || !hotkey.IsValid)
        {
            SetHotkeyState(_pendingHotkey, "Defina e valide um atalho antes de continuar.", isValid: false);
            return false;
        }

        return true;
    }

    // ==================================================================
    // Salvar / gravar
    // ==================================================================
    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadEditor(out string name, out HotkeyDefinition hotkey))
        {
            return;
        }

        if (_editingAutomationId is Guid editId && Find(editId) is { } automation)
        {
            await SaveEditedAutomationAsync(automation, name, hotkey);
            return;
        }

        await BeginRecordingAsync(name, hotkey, existingAutomationId: null);
    }

    private async void RecordAgain_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadEditor(out string name, out HotkeyDefinition hotkey))
        {
            return;
        }

        await BeginRecordingAsync(name, hotkey, _editingAutomationId);
    }

    /// <summary>Salva nome/atalho de uma automação existente sem regravar os eventos.</summary>
    private async Task SaveEditedAutomationAsync(Automation automation, string name, HotkeyDefinition hotkey)
    {
        string previousName = automation.Name;
        HotkeyDefinition previousHotkey = automation.Hotkey;
        Guid automationId = automation.Id;

        automation.Name = name;
        automation.Hotkey = hotkey.Clone();

        CloseEditor();
        var failures = _hotkeys.Resume(_automations);

        if (failures.TryGetValue(automation.Id, out string? error))
        {
            automation.Name = previousName;
            automation.Hotkey = previousHotkey;
            RefreshList();
            OpenEditor(automationId, name, hotkey, error);
            MessageBox.Show(this, $"Não foi possível registrar {hotkey.Display}.\n\n{error}", "Atalho indisponível",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await SaveAsync())
        {
            SetStatus($"Automação '{automation.Name}' atualizada.");
        }

        RefreshList();
        UpdateTrayState();
    }

    /// <summary>Cria (ou prepara) a automação, registra o atalho e inicia a gravação.</summary>
    private async Task BeginRecordingAsync(string name, HotkeyDefinition hotkey, Guid? existingAutomationId)
    {
        Automation automation;
        bool isNew = existingAutomationId is null;
        string? previousName = null;
        HotkeyDefinition? previousHotkey = null;

        if (existingAutomationId is Guid editId && Find(editId) is { } existing)
        {
            automation = existing;
            previousName = automation.Name;
            previousHotkey = automation.Hotkey;
            automation.Name = name;
            automation.Hotkey = hotkey.Clone();
            automation.Events = new List<MouseEventRecord>(); // regravação substitui a sequência
        }
        else
        {
            automation = new Automation { Name = name, Hotkey = hotkey.Clone() };
            _automations.Add(automation);
        }

        CloseEditor();
        var failures = _hotkeys.Resume(_automations);

        if (failures.TryGetValue(automation.Id, out string? error))
        {
            if (isNew)
            {
                _automations.Remove(automation);
            }
            else
            {
                automation.Name = previousName!;
                automation.Hotkey = previousHotkey!;
            }

            RefreshList();
            OpenEditor(existingAutomationId, name, hotkey, error);
            MessageBox.Show(this, $"Não foi possível registrar {hotkey.Display}.\n\n{error}", "Atalho indisponível",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!await SaveAsync())
        {
            UpdateTrayState();
            return;
        }

        RefreshList();
        StartRecording(automation);
    }

    private void StartRecording(Automation automation)
    {
        try
        {
            CancelPlayback();

            _recorder?.Dispose();
            _recorder = new AutomationRecorder();
            _recorder.Start();
            _recordingAutomationId = automation.Id;

            HideToTray(showHint: false);
            _tray.ShowBalloon("Gravação iniciada", $"Pressione {automation.Hotkey.Display} para parar.");
            SetStatus($"Gravando '{automation.Name}' — pressione {automation.Hotkey.Display} para parar.", DangerBrush);
            UpdateTrayState();
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao iniciar a gravação.", ex);
            _recorder?.Dispose();
            _recorder = null;
            _recordingAutomationId = null;
            RestoreWindow();
            UpdateTrayState();
            MessageBox.Show(this, $"Não foi possível iniciar a gravação.\n\n{ex.Message}", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopRecordingAsync()
    {
        AutomationRecorder? recorder = _recorder;
        if (recorder is null)
        {
            return;
        }

        _recorder = null;

        List<MouseEventRecord> events;
        try
        {
            events = recorder.Stop();
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao finalizar a gravação.", ex);
            events = new List<MouseEventRecord>();
        }
        finally
        {
            recorder.Dispose();
        }

        Guid? automationId = _recordingAutomationId;
        _recordingAutomationId = null;

        RestoreWindow();

        Automation? automation = automationId is Guid id ? Find(id) : null;
        if (automation is not null)
        {
            automation.Events = events;
        }

        await SaveAsync();
        RefreshList();
        UpdateTrayState();

        if (events.Count == 0)
        {
            _tray.ShowBalloon("Nenhum evento gravado",
                "A gravação terminou sem cliques. Tente novamente com a janela minimizada.",
                WinForms.ToolTipIcon.Warning);
            SetStatus("Gravação finalizada sem eventos. Use Editar → Regravar.", MutedBrush);
            return;
        }

        _tray.ShowBalloon("Gravação concluída", $"{events.Count} evento(s) salvos em '{automation?.Name}'.");
        SetStatus($"Gravação concluída: {events.Count} evento(s) em '{automation?.Name}'.");
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            await _store.SaveAsync(_automations);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao salvar as automações.", ex);
            MessageBox.Show(this, ex.Message, "Não foi possível salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // ==================================================================
    // Reprodução
    // ==================================================================
    private void OnHotkeyPressed(Guid automationId)
    {
        // Chamado na thread da UI (hook da janela invisível).
        if (_recorder is { IsRecording: true })
        {
            if (_recordingAutomationId == automationId)
            {
                _ = StopRecordingAsync();
            }

            return;
        }

        if (_player.IsPlaying)
        {
            if (_player.CurrentAutomationId == automationId)
            {
                CancelPlayback();
                SetStatus("Reprodução cancelada.", MutedBrush);
            }

            return;
        }

        if (Find(automationId) is not { } automation)
        {
            return;
        }

        if (automation.Events.Count == 0)
        {
            _tray.ShowBalloon("Nada para reproduzir", $"'{automation.Name}' ainda não tem eventos gravados.",
                WinForms.ToolTipIcon.Info);
            return;
        }

        _ = PlayAutomationAsync(automation);
    }

    private async void PlayAutomation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Automation automation)
        {
            return;
        }

        if (_recorder is { IsRecording: true })
        {
            MessageBox.Show(this, "Pare a gravação atual antes de reproduzir uma automação.", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_player.IsPlaying)
        {
            MessageBox.Show(this, "Já existe uma reprodução em andamento. Pressione o mesmo atalho para cancelar.",
                "MOUSEMAPPER", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (automation.Events.Count == 0)
        {
            MessageBox.Show(this, "Esta automação ainda não possui eventos gravados.", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await PlayAutomationAsync(automation);
    }

    private async Task PlayAutomationAsync(Automation automation)
    {
        _playbackCts?.Dispose();
        var cts = new CancellationTokenSource();
        _playbackCts = cts;

        SetStatus($"▶ Reproduzindo '{automation.Name}'… ({automation.Hotkey.Display} cancela)", AccentBrush);
        UpdateTrayState();

        try
        {
            // Thread de fundo: a UI continua responsiva e o cancelamento é imediato.
            await Task.Run(() => _player.PlayAsync(automation, cts.Token), cts.Token);
            SetStatus($"Reprodução de '{automation.Name}' concluída.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Reprodução cancelada.", MutedBrush);
        }
        catch (Exception ex)
        {
            _log.Error($"Falha ao reproduzir '{automation.Name}'.", ex);
            MessageBox.Show(this, $"Falha ao reproduzir a automação.\n\n{ex.Message}", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Falha na reprodução.", DangerBrush);
        }
        finally
        {
            if (ReferenceEquals(_playbackCts, cts))
            {
                _playbackCts = null;
            }

            cts.Dispose();
            UpdateTrayState();
        }
    }

    private void CancelPlayback()
    {
        try
        {
            _playbackCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Reprodução já finalizada.
        }
    }

    // ==================================================================
    // Excluir
    // ==================================================================
    private async void DeleteAutomation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Automation automation)
        {
            return;
        }

        if (_recorder is { IsRecording: true })
        {
            MessageBox.Show(this, "Pare a gravação atual antes de excluir automações.", "MOUSEMAPPER",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Excluir a automação \"{automation.Name}\"?\n\nEssa ação não pode ser desfeita.",
            "Excluir automação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _hotkeys.Unregister(automation.Id);
        _automations.Remove(automation);

        if (await SaveAsync())
        {
            SetStatus($"Automação '{automation.Name}' excluída.");
        }

        RefreshList();
        UpdateTrayState();
    }

    // ==================================================================
    // Bandeja / janela
    // ==================================================================
    private void HideToTray(bool showHint)
    {
        // Se o editor estava aberto, cancela a edição e devolve os atalhos ao ar
        // (eles ficam suspensos enquanto o usuário escolhe a combinação).
        if (EditorOverlay.Visibility == Visibility.Visible)
        {
            CloseEditor();
            var failures = _hotkeys.Resume(_automations);
            ReportRegistrationFailures(failures, showDialog: false);
            SetStatus("Edição cancelada ao minimizar a janela.", MutedBrush);
        }

        Hide();

        if (!showHint || _trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _tray.ShowBalloon("MOUSEMAPPER continua rodando",
            "Clique no ícone da bandeja para reabrir. Para encerrar: botão direito → Sair.");
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RestoreWindow() => ShowFromTray();

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        var confirm = MessageBox.Show(
            IsVisible ? this : null,
            "Encerrar o MOUSEMAPPER?\n\nAs automações salvas continuam disponíveis na próxima abertura.",
            "Sair",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _isExiting = true;

        try
        {
            if (_recorder is { IsRecording: true })
            {
                var events = _recorder.Stop();
                if (_recordingAutomationId is Guid id && Find(id) is { } automation && events.Count > 0)
                {
                    automation.Events = events;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao finalizar a gravação durante a saída.", ex);
        }

        _recorder?.Dispose();
        _recorder = null;

        CancelPlayback();

        try
        {
            _store.Save(_automations);
        }
        catch (Exception ex)
        {
            _log.Error("Falha ao salvar durante a saída.", ex);
        }

        // Libera hooks e atalhos globais antes de fechar.
        _hotkeys.Dispose();
        _tray.Dispose();

        Close();
        Application.Current.Shutdown();
    }

    // ==================================================================
    // Status / utilidades
    // ==================================================================
    private void SetStatus(string text, Brush? dotColor = null)
    {
        StatusText.Text = text;
        StatusDot.Fill = dotColor ?? SuccessBrush;
    }

    private void UpdateTrayState()
    {
        bool recording = _recorder is { IsRecording: true };
        _tray.IsRecording = recording;
        _tray.IsPlaying = _player.IsPlaying;

        _tray.SetTooltip(recording
            ? "MOUSEMAPPER — gravando…"
            : _player.IsPlaying
                ? "MOUSEMAPPER — reproduzindo…"
                : "MOUSEMAPPER");
    }

    private void RefreshList()
    {
        AutomationsGrid.Items.Refresh();
        UpdateEmptyState();

        DataPathText.Text = $"Dados: {_store.FilePath}";
        DataPathText.ToolTip = _store.FilePath;
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _automations.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        AutomationsGrid.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private Automation? Find(Guid id) => _automations.FirstOrDefault(a => a.Id == id);

    private void ReportRegistrationFailures(IReadOnlyDictionary<Guid, string> failures, bool showDialog)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var lines = failures.Select(pair =>
        {
            Automation? automation = Find(pair.Key);
            return $"• {(automation?.Name ?? pair.Key.ToString())}: {pair.Value}";
        }).ToList();

        _log.Warn("Atalhos não registrados: " + string.Join(" | ", lines));

        if (!showDialog)
        {
            _tray.ShowBalloon(
                "Atalhos indisponíveis",
                $"{failures.Count} atalho(s) não puderam ser registrados. Use Editar para escolher outra combinação.",
                WinForms.ToolTipIcon.Warning);
            return;
        }

        MessageBox.Show(
            this,
            "Alguns atalhos não puderam ser registrados e não funcionarão até serem alterados:\n\n" +
            string.Join("\n", lines),
            "Atalhos indisponíveis",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private void TryApplyDarkTitleBar()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            int result = NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            if (result != 0)
            {
                // Builds antigos do Windows 10 usam o atributo 19.
                NativeMethods.DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Não foi possível ativar a barra de título escura.", ex);
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mousemapper.ico"));
            if (resource is not null)
            {
                return new System.Drawing.Icon(resource.Stream);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warn("Não foi possível carregar o ícone personalizado; usando o padrão.", ex);
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
