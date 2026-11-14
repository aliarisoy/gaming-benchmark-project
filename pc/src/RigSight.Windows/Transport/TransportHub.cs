using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Protocol;
using RigSight.Core.Storage;

namespace RigSight.Windows.Transport;

/// <summary>Fans a message out to every connected transport and merges their status.</summary>
public sealed class TransportHub : ITransportHub, IAsyncDisposable
{
    private readonly WebSocketHub _webSocket;
    private readonly BleServer _ble;
    private readonly bool _enableBle;
    private readonly object _gate = new();

    public TransportHub(ConfigStore config, SessionStore store, PairingCoordinator pairing, bool enableBle = true)
    {
        Pairing = pairing;
        _enableBle = enableBle;
        _webSocket = new WebSocketHub(config, store, pairing, config.Current.WsPort);
        _ble = new BleServer(config, pairing);
        _webSocket.StatusChanged += RaiseStatus;
        _ble.StatusChanged += RaiseStatus;
    }

    public PairingCoordinator Pairing { get; }

    public TransportStatus Status
    {
        get
        {
            var errors = new[] { _webSocket.LastError, _ble.LastError }
                .Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
            var clients = _webSocket.Clients.Concat(_ble.Clients).Distinct().ToArray();
            return new TransportStatus(
                _webSocket.IsRunning,
                _webSocket.LanAccessible,
                _enableBle && _ble.IsRunning,
                _webSocket.ClientCount,
                _enableBle ? _ble.ClientCount : 0,
                _webSocket.Urls,
                clients,
                errors.Length == 0 ? null : string.Join(" · ", errors));
        }
    }

    public event Action? StatusChanged;

    public async Task StartAsync()
    {
        await _webSocket.StartAsync();
        // A loopback-only listener is useless to a phone; keep the BLE handshake
        // from advertising Wi-Fi hosts the phone cannot reach.
        _ble.AdvertiseWiFi = _webSocket.LanAccessible;
        if (_enableBle) await _ble.StartAsync();
        RaiseStatus();
        Log.Info($"Transport hub started: Wi-Fi={_webSocket.IsRunning}, BLE={_enableBle && _ble.IsRunning}.");
    }

    public async Task StopAsync()
    {
        if (_enableBle) await _ble.StopAsync();
        await _webSocket.StopAsync();
        RaiseStatus();
    }

    public async Task BroadcastAsync(WireMessage message)
    {
        await _webSocket.BroadcastAsync(message);
        await _ble.SendAsync(message);
    }

    public void SetCommandHandler(Func<WireMessage, ITransportClient?, Task> handler)
    {
        lock (_gate)
        {
            _webSocket.CommandHandler = handler;
            _ble.CommandHandler = handler;
        }
    }

    public void SetSnapshotProvider(Func<TransportSnapshot> provider)
    {
        lock (_gate)
        {
            _webSocket.SnapshotProvider = provider;
            _ble.SnapshotProvider = provider;
        }
    }

    private void RaiseStatus()
    {
        try { StatusChanged?.Invoke(); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _webSocket.DisposeAsync();
        await _ble.DisposeAsync();
    }
}
