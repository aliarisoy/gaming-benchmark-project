using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using RigSight.Agent.ViewModels;
using WinForms = System.Windows.Forms;

namespace RigSight.Agent;

/// <summary>Windows tray presence: quick start/stop, pairing, and exit.</summary>
public sealed class TrayController : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly WinForms.NotifyIcon _icon;
    private readonly Icon _appIcon;
    private readonly IntPtr _iconHandle;
    private readonly WinForms.ToolStripMenuItem _startItem;
    private readonly WinForms.ToolStripMenuItem _stopItem;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _hinted;

    public TrayController(MainViewModel vm)
    {
        _vm = vm;
        (_appIcon, _iconHandle) = CreateIcon();

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(18, 24, 34),
            ForeColor = Color.FromArgb(241, 245, 248),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9f),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColorTable()),
        };

        var open = new WinForms.ToolStripMenuItem("Open RigSight") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        open.Click += (_, _) => Raise(OpenRequested);

        _startItem = new WinForms.ToolStripMenuItem("Start tracking foreground game");
        _startItem.Click += (_, _) => Raise(StartRequested);

        _stopItem = new WinForms.ToolStripMenuItem("Stop session");
        _stopItem.Click += (_, _) => Raise(StopRequested);

        var pair = new WinForms.ToolStripMenuItem("Pair a phone…");
        pair.Click += (_, _) => Raise(PairRequested);

        var exit = new WinForms.ToolStripMenuItem("Exit RigSight");
        exit.Click += (_, _) => Raise(ExitRequested);

        menu.Items.Add(open);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(pair);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "RigSight",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => Raise(OpenRequested);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public event Action? OpenRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? PairRequested;
    public event Action? ExitRequested;

    public void ShowHint()
    {
        if (_hinted) return;
        _hinted = true;
        try
        {
            _icon.ShowBalloonTip(2500, "RigSight is still running",
                "The game screen stays clean. Open RigSight from this tray icon any time.", WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // Balloons are optional.
        }
    }

    public void ShowBalloon(string title, string message)
    {
        try { _icon.ShowBalloonTip(3000, title, message, WinForms.ToolTipIcon.Info); }
        catch { /* ignore */ }
    }

    private void Refresh()
    {
        var text = _vm.TrayStatus;
        if (text.Length > 63) text = text[..60] + "…";
        try { _icon.Text = text; } catch { /* shell not ready */ }
        _startItem.Enabled = _vm.CanStartSession;
        _stopItem.Enabled = _vm.IsRecording;
    }

    private static void Raise(Action? action)
    {
        if (action is null) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private static (Icon Icon, IntPtr Handle) CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        using var accent = new SolidBrush(Color.FromArgb(110, 243, 197));
        graphics.FillEllipse(accent, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(6, 17, 13));
        var size = graphics.MeasureString("R", font);
        graphics.DrawString("R", font, ink, (32 - size.Width) / 2f, (32 - size.Height) / 2f - 1f);

        var handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(18, 24, 34);
        private static readonly Color Hover = Color.FromArgb(30, 39, 54);
        private static readonly Color Border = Color.FromArgb(51, 65, 87);

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
        public override Color MenuItemPressedGradientBegin => Surface;
        public override Color MenuItemPressedGradientEnd => Surface;
    }
}
