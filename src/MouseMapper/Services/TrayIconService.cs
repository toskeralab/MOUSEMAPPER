using System.Drawing;
using WinForms = System.Windows.Forms;

namespace MouseMapper.Services;

/// <summary>
/// Ícone na bandeja do sistema (o WPF não possui API nativa; usamos o NotifyIcon do
/// WinForms, que já vem no runtime Windows Desktop — nenhum pacote extra é necessário).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _stopRecordingItem;
    private readonly WinForms.ToolStripMenuItem _cancelPlaybackItem;
    private bool _disposed;

    public TrayIconService(Icon icon)
    {
        var openItem = new WinForms.ToolStripMenuItem("Abrir MOUSEMAPPER");
        openItem.Click += (_, _) => OpenRequested?.Invoke();

        _stopRecordingItem = new WinForms.ToolStripMenuItem("Parar gravação");
        _stopRecordingItem.Click += (_, _) => StopRecordingRequested?.Invoke();

        _cancelPlaybackItem = new WinForms.ToolStripMenuItem("Cancelar reprodução");
        _cancelPlaybackItem.Click += (_, _) => CancelPlaybackRequested?.Invoke();

        var exitItem = new WinForms.ToolStripMenuItem("Sair");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(23, 26, 35),
            ForeColor = Color.FromArgb(242, 245, 250),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkMenuColorTable()),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9F)
        };

        menu.Items.Add(openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_stopRecordingItem);
        menu.Items.Add(_cancelPlaybackItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) => RefreshMenuState();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "MOUSEMAPPER",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? StopRecordingRequested;

    public event Action? CancelPlaybackRequested;

    public event Action? ExitRequested;

    public bool IsRecording { get; set; }

    public bool IsPlaying { get; set; }

    public void ShowBalloon(string title, string message, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warn("Não foi possível exibir a notificação da bandeja.", ex);
        }
    }

    public void SetTooltip(string text)
    {
        // NotifyIcon.Text aceita no máximo 63 caracteres.
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "...";
    }

    private void RefreshMenuState()
    {
        _stopRecordingItem.Enabled = IsRecording;
        _cancelPlaybackItem.Enabled = IsPlaying;
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
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warn("Falha ao liberar o ícone da bandeja.", ex);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Menu de contexto escuro para combinar com o tema do app.</summary>
    private sealed class DarkMenuColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(23, 26, 35);
        private static readonly Color Hover = Color.FromArgb(46, 54, 74);
        private static readonly Color Border = Color.FromArgb(42, 48, 64);

        public override Color ToolStripDropDownBackground => Surface;

        public override Color ImageMarginGradientBegin => Surface;

        public override Color ImageMarginGradientMiddle => Surface;

        public override Color ImageMarginGradientEnd => Surface;

        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => Border;

        public override Color MenuItemSelected => Hover;

        public override Color MenuItemSelectedGradientBegin => Hover;

        public override Color MenuItemSelectedGradientEnd => Hover;

        public override Color SeparatorDark => Border;

        public override Color SeparatorLight => Border;
    }
}
