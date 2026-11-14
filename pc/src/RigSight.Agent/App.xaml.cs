using System.Windows;
using System.Windows.Threading;
using RigSight.Agent.ViewModels;
using RigSight.Core.Logging;

namespace RigSight.Agent;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private AgentHost? _host;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayController? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, @"Global\RigSight.Agent.Singleton", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show(
                "RigSight is already running. Look for the mint R in the notification area.",
                "RigSight",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled application exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved background task exception.", args.Exception);
            args.SetObserved();
        };

        try
        {
            var options = AgentOptions.Parse(e.Args);
            _host = new AgentHost(options);
            _viewModel = new MainViewModel(_host);
            _window = new MainWindow(_host, _viewModel);
            MainWindow = _window;

            _tray = new TrayController(_viewModel);
            _tray.OpenRequested += () => _window.ShowFromTray();
            _tray.StartRequested += () => _ = _viewModel.RunStartForegroundAsync();
            _tray.StopRequested += () => _ = _viewModel.RunStopAsync();
            _tray.PairRequested += () => _viewModel.ShowPairing();
            _tray.ExitRequested += () => _window.ExitApplication();

            _viewModel.ShowRequested += () => _window.ShowFromTray();
            _viewModel.ExitRequested += () => _window.ExitApplication();
            _window.HiddenToTray += () => _tray.ShowHint();

            _window.Show();
            if (options.StartMinimized || _host.Config.Current.StartMinimized)
            {
                _window.Hide();
                _tray.ShowHint();
            }

            await _host.StartAsync();
            Log.Info("Agent UI ready.");

            if (options.Demo)
            {
                await Task.Delay(350);
                await _viewModel.StartDemoCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Log.Error("RigSight failed to start.", ex);
            MessageBox.Show(
                $"RigSight could not start.\n\n{ex.Message}\n\nLogs: {RigSight.Core.Storage.RigSightPaths.LogsDirectory}",
                "RigSight",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            $"Something went wrong, but RigSight is still running.\n\n{e.Exception.Message}\n\nDetails were written to the log.",
            "RigSight",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();

        if (_host is not null)
        {
            try
            {
                // Run off the UI thread so awaited continuations cannot deadlock shutdown.
                Task.Run(() => _host.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Log.Warn($"Shutdown cleanup failed: {ex.Message}");
            }
        }

        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
