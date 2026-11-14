using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using RigSight.Core.Models;
using RigSight.Core.Security;
using RigSight.Core.Storage;

namespace RigSight.Windows.Transport;

public sealed record TransportSnapshot(
    SessionInfo? Session,
    PromptInfo? Prompt,
    TelemetrySample? LastSample);

public interface ITransportClient
{
    string Id { get; }
    string? DeviceName { get; }
    string TransportName { get; }
    Task SendAsync(Core.Protocol.WireMessage message, CancellationToken ct = default);
}

public sealed record TransportStatus(
    bool WebSocketRunning,
    bool WebSocketLanAccessible,
    bool BleRunning,
    int WebSocketClients,
    int BleClients,
    IReadOnlyList<string> WsUrls,
    IReadOnlyList<string> Clients,
    string? LastError);

public interface ITransportHub
{
    TransportStatus Status { get; }
    event Action? StatusChanged;
    Task StartAsync();
    Task StopAsync();
    Task BroadcastAsync(Core.Protocol.WireMessage message);
    void SetCommandHandler(Func<Core.Protocol.WireMessage, ITransportClient?, Task> handler);
    void SetSnapshotProvider(Func<TransportSnapshot> provider);
}

internal static class NetworkHelper
{
    public static List<string> LanAddresses()
    {
        var results = new List<(int priority, string address)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0 &&
                    nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                    continue;

                var score = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 0,
                    NetworkInterfaceType.Ethernet => 1,
                    _ => 3,
                };

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                    results.Add((score, ip));
                }
            }
        }
        catch
        {
            // Fall back to the DNS host entry below.
        }

        if (results.Count == 0)
        {
            try
            {
                results.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => (9, a.ToString())));
            }
            catch { /* ignore */ }
        }

        return results.OrderBy(r => r.priority).Select(r => r.address).Distinct().Take(6).ToList();
    }
}

/// <summary>Issues and validates phone tokens, shared by BLE and Wi-Fi pairing.</summary>
public sealed class PairingCoordinator
{
    private readonly ConfigStore _config;
    private readonly PairingSession _session;
    private readonly object _gate = new();

    public PairingCoordinator(ConfigStore config, PairingSession session)
    {
        _config = config;
        _session = session;
    }

    public PairingSession Session => _session;

    public PairedDevice? FindByToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        foreach (var device in _config.Current.PairedDevices.ToArray())
        {
            var candidate = System.Text.Encoding.UTF8.GetBytes(device.Token);
            if (candidate.Length == bytes.Length &&
                CryptographicOperations.FixedTimeEquals(candidate, bytes))
                return device;
        }
        return null;
    }

    public PairedDevice? PairWithCode(string? code, string? deviceName, out string? error)
    {
        error = null;
        var result = _session.TryRedeem(code);
        if (result != PairCodeResult.Success)
        {
            error = result switch
            {
                PairCodeResult.Expired => "Pairing code expired. Generate a new code on the PC.",
                PairCodeResult.LockedOut => "Too many attempts. Generate a new code on the PC.",
                _ => "Incorrect pairing code.",
            };
            return null;
        }

        lock (_gate)
        {
            var device = new PairedDevice
            {
                Name = string.IsNullOrWhiteSpace(deviceName) ? "Android phone" : deviceName!,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .Replace('+', '-').Replace('/', '_').TrimEnd('='),
                PairedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            _config.Current.PairedDevices.Add(device);
            _config.Save();
            return device;
        }
    }

    public void Touch(PairedDevice device)
    {
        device.LastSeenAt = DateTimeOffset.UtcNow;
        _config.Save();
    }

    public void RevokeAll()
    {
        lock (_gate)
        {
            _config.Current.PairedDevices.Clear();
            _config.Save();
        }
    }

    public void Revoke(string deviceId)
    {
        lock (_gate)
        {
            _config.Current.PairedDevices.RemoveAll(d => d.Id == deviceId);
            _config.Save();
        }
    }
}
