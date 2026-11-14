using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RigSight.Core.Models;

namespace RigSight.Agent;

public partial class PromptWindow : Window
{
    private readonly Func<Task<bool>> _start;
    private readonly Func<Task<bool>> _decline;
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _visibleUntil;
    private bool _busy;

    public PromptWindow(PromptInfo prompt, Func<Task<bool>> start, Func<Task<bool>> decline)
    {
        InitializeComponent();
        _start = start;
        _decline = decline;
        GameNameText.Text = prompt.GameName;

        _visibleUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        try
        {
            var expires = DateTimeOffset.FromUnixTimeMilliseconds(prompt.ExpiresAt);
            if (expires < _visibleUntil) _visibleUntil = expires;
        }
        catch
        {
            // Keep the local visibility window.
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Closed += (_, _) => _timer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 22;
        Top = area.Bottom - ActualHeight - 22;

        var slide = new TranslateTransform(0, 18);
        RootCard.RenderTransform = slide;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var remaining = _visibleUntil - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            Close();
            return;
        }

        CountdownText.Text = $"AUTO-HIDES IN {(int)Math.Ceiling(remaining.TotalSeconds)} S";
    }

    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "Starting telemetry on the PC…");
        try
        {
            var started = await _start();
            if (started)
            {
                if (IsVisible) Close();
            }
            else
            {
                SetBusy(false, "Could not start. The game may have exited — use Games to retry.");
            }
        }
        catch (Exception ex)
        {
            SetBusy(false, $"Could not start: {ex.Message}");
        }
    }

    private async void OnDeclineClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "Dismissing…");
        try
        {
            await _decline();
            if (IsVisible) Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, $"Could not dismiss: {ex.Message}");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        StartButton.IsEnabled = !busy;
        DeclineButton.IsEnabled = !busy;
        StartButton.Content = busy ? "Working…" : "Start tracking";
        StatusText.Text = status;
    }
}
