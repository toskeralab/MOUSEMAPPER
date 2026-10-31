using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MouseMapper.Interop;
using MouseMapper.Models;
using MouseMapper.Services;

namespace MouseMapper.Views;

/// <summary>
/// Janela modal que captura a combinação de teclas usando WH_KEYBOARD_LL.
/// O hook (e não o PreviewKeyDown do WPF) é usado para que a captura continue
/// funcionando mesmo se a janela perder o foco e para bloquear o repasse das teclas.
/// </summary>
public partial class HotkeyCaptureWindow : Window
{
    private readonly KeyboardHook _hook = new();
    private readonly Brush _brushSuccess;
    private readonly Brush _brushDanger;
    private readonly Brush _brushText;
    private readonly Brush _brushMuted;
    private bool _closing;

    public HotkeyCaptureWindow()
    {
        InitializeComponent();

        _brushSuccess = (Brush)FindResource("BrushSuccess");
        _brushDanger = (Brush)FindResource("BrushDanger");
        _brushText = (Brush)FindResource("BrushText");
        _brushMuted = (Brush)FindResource("BrushTextMuted");

        _hook.KeyEvent += OnKeyEvent;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Combinação escolhida (null quando o usuário cancela).</summary>
    public HotkeyDefinition? Result { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        Keyboard.Focus(this);

        try
        {
            _hook.Suppress = true; // as teclas não vazam para o app em foco
            _hook.Start();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao instalar o hook de teclado.", ex);
            MessageBox.Show(
                "Não foi possível capturar as teclas (hook de teclado indisponível).\n\n" +
                $"Detalhe: {ex.Message}",
                "MOUSEMAPPER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            CloseDialog(false);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hook.KeyEvent -= OnKeyEvent;
        _hook.Dispose();
    }

    private void OnKeyEvent(object? sender, KeyboardHookEventArgs e)
    {
        try
        {
            HandleKey(e);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao processar tecla capturada.", ex);
        }
    }

    private void HandleKey(KeyboardHookEventArgs e)
    {
        if (_closing || !e.IsKeyDown)
        {
            return;
        }

        uint virtualKey = (uint)e.VirtualKey;
        uint modifiers = HotkeyFormatter.ReadCurrentModifiers();

        // Modificador isolado: mostra o preview parcial e continua aguardando.
        if (HotkeyFormatter.IsModifierKey(virtualKey))
        {
            string preview = HotkeyFormatter.Describe(modifiers, 0);
            PreviewText.Text = preview.Length == 0 ? "Aguardando teclas…" : preview + " + …";
            PreviewText.Foreground = _brushText;
            HintText.Text = "Agora pressione a tecla principal.";
            HintText.Foreground = _brushMuted;
            return;
        }

        // ESC sem modificadores cancela.
        if (virtualKey == NativeMethods.VK_ESCAPE && modifiers == 0)
        {
            CloseDialog(false);
            return;
        }

        if (modifiers == 0 && !HotkeyFormatter.IsAllowedStandalone(virtualKey))
        {
            PreviewText.Text = HotkeyFormatter.DescribeKey(virtualKey);
            PreviewText.Foreground = _brushDanger;
            HintText.Text = "Adicione Ctrl, Alt, Shift ou Win (ou use uma tecla F1–F12).";
            HintText.Foreground = _brushDanger;
            return;
        }

        Result = new HotkeyDefinition
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            Display = HotkeyFormatter.Describe(modifiers, virtualKey)
        };

        PreviewText.Text = Result.Display;
        PreviewText.Foreground = _brushSuccess;
        HintText.Text = "Combinação capturada.";
        HintText.Foreground = _brushSuccess;

        CloseDialog(true);
    }

    private void CloseDialog(bool? dialogResult)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        // Fecha fora do callback do hook para evitar reentrância na bomba de mensagens.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    DialogResult = dialogResult;
                }
                catch
                {
                    Close();
                }
            }),
            DispatcherPriority.Background);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);
}
