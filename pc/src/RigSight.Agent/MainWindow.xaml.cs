using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RigSight.Agent.ViewModels;
using RigSight.Core.Logging;
using RigSight.Core.Models;

namespace RigSight.Agent;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private HwndSource? _source;
    private PromptWindow? _promptWindow;
    private bool _forceClose;
    private bool _startHotkey;
    private bool _stopHotkey;

    private const int HotkeyStart = 0x9001;
    private const int HotkeyStop = 0x9002;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    public MainWindow(AgentHost host, MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.PromptRequested += OnPromptRequested;
        vm.PromptDismissed += OnPromptDismissed;
    }

    public event Action? HiddenToTray;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        ApplyWindowAppearance(handle);

        _startHotkey = RegisterHotKey(handle, HotkeyStart, ModControl | ModAlt | ModNoRepeat, 0x47); // Ctrl+Alt+G
        _stopHotkey = RegisterHotKey(handle, HotkeyStop, ModControl | ModAlt | ModNoRepeat, 0x53);  // Ctrl+Alt+S
        if (!_startHotkey) Log.Warn("Global hotkey Ctrl+Alt+G is already taken.");
        if (!_stopHotkey) Log.Warn("Global hotkey Ctrl+Alt+S is already taken.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        handled = true;
        var id = wParam.ToInt32();

        if (id == HotkeyStart)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (_vm.PendingPrompt is { } prompt)
                    await _vm.StartPromptAsync(prompt);
                else
                    await _vm.RunStartForegroundAsync();
            });
        }
        else if (id == HotkeyStop)
        {
            _ = Dispatcher.InvokeAsync(async () => await _vm.RunStopAsync());
        }

        return IntPtr.Zero;
    }

    private static void ApplyWindowAppearance(IntPtr handle)
    {
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            var corners = 2;
            DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch
        {
            // Pre-Windows 11 keeps the standard chrome.
        }
    }

    private void OnPromptRequested(PromptInfo prompt)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnPromptRequested(prompt));
            return;
        }

        _promptWindow?.Close();
        var window = new PromptWindow(
            prompt,
            start: () => _vm.StartPromptAsync(prompt),
            decline: () => _vm.DeclinePromptAsync(prompt));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_promptWindow, window)) _promptWindow = null;
        };
        _promptWindow = window;
        window.Show();
    }

    private void OnPromptDismissed()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnPromptDismissed);
            return;
        }
        _promptWindow?.Close();
        _promptWindow = null;
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ExitApplication()
    {
        _forceClose = true;
        _promptWindow?.Close();
        Application.Current.Shutdown();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_forceClose)
        {
            Hide();
            HiddenToTray?.Invoke();
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PromptRequested -= OnPromptRequested;
        _vm.PromptDismissed -= OnPromptDismissed;

        var handle = new WindowInteropHelper(this).Handle;
        if (_startHotkey) UnregisterHotKey(handle, HotkeyStart);
        if (_stopHotkey) UnregisterHotKey(handle, HotkeyStop);
        _source?.RemoveHook(WndProc);

        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
