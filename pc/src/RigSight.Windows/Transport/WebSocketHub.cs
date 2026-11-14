using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Protocol;
using RigSight.Core.Storage;

namespace RigSight.Windows.Transport;

/// <summary>LAN WebSocket endpoint used for high-rate metrics, control, and history.</summary>
public sealed class WebSocketHub : IAsyncDisposable
{
    private readonly ConfigStore _config;
    private readonly SessionStore _store;
    private readonly PairingCoordinator _pairing;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, WsClient> _clients = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public WebSocketHub(ConfigStore config, SessionStore store, PairingCoordinator pairing, int port)
    {
        _config = config;
        _store = store;
        _pairing = pairing;
        _port = port;
    }

    public bool IsRunning { get; private set; }
    /// <summary>True when the listener accepts connections from other devices, not just this PC.</summary>
    public bool LanAccessible { get; private set; }
    public string? LastError { get; private set; }
    public IReadOnlyList<string> Urls { get; private set; } = [];
    public int ClientCount => _clients.Count;
    public IReadOnlyList<string> Clients => _clients.Values
        .Where(c => c.IsAuthenticated)
        .Select(c => c.DeviceName ?? "Phone")
        .ToArray();

    public Func<WireMessage, ITransportClient?, Task>? CommandHandler { get; set; }
    public Func<TransportSnapshot>? SnapshotProvider { get; set; }
    public event Action? StatusChanged;

    public async Task StartAsync()
    {
        if (IsRunning) return;
        var prefixes = new[] { $"http://+:{_port}/", $"http://localhost:{_port}/" };
        string? boundPrefix = null;
        Exception? last = null;

        foreach (var prefix in prefixes)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _listener = listener;
                boundPrefix = prefix;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"WebSocket listener could not bind {prefix}: {ex.Message}");
            }
        }

        if (_listener is null)
        {
            LastError = $"WebSocket server could not start: {last?.Message}";
            Log.Error(LastError);
            return;
        }

        IsRunning = true;
        LanAccessible = boundPrefix is not null &&
                        boundPrefix.StartsWith("http://+", StringComparison.Ordinal);
        if (LanAccessible)
        {
            await FirewallHelper.EnsureInboundRuleAsync(_port);
            LastError = null;
            Urls = NetworkHelper.LanAddresses()
                .Select(ip => $"ws://{ip}:{_port}/v1/stream")
                .ToArray();
        }
        else
        {
            // HttpListener falls back to localhost when it lacks URL ACL/admin
            // rights. Do not pretend the phone can reach this address.
            LastError = "Wi-Fi is reachable on this PC only. Run RigSight as administrator so your phone can connect.";
            Urls = [$"ws://localhost:{_port}/v1/stream"];
        }

        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Log.Info($"WebSocket server listening on port {_port} ({(LanAccessible ? "LAN" : "localhost only")}). {string.Join(", ", Urls)}");
        RaiseStatus();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested || _listener is not { IsListening: true })
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"WebSocket accept failed: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleContextAsync(context, ct), ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            if (!context.Request.IsWebSocketRequest)
            {
                if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/")
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        name = "RigSight",
                        version = BuildInfo.Version,
                        protocolVersion = BuildInfo.ProtocolVersion,
                        pc = _config.Current.PcName,
                        paired = _config.Current.PairedDevices.Count,
                        urls = Urls,
                    }, ProtocolJson.Options);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, ct);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
                context.Response.Close();
                return;
            }

            if (!context.Request.Url!.AbsolutePath.StartsWith("/v1/stream", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var wsContext = await context.AcceptWebSocketAsync(null);
            var socket = wsContext.WebSocket;
            var client = new WsClient(socket);
            client.StartSender(ct);

            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            string? helloJson;
            try { helloJson = await ReceiveTextAsync(socket, helloTimeout.Token); }
            catch (OperationCanceledException) { helloJson = null; }

            if (helloJson is null)
            {
                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.Error,
                    Code = "no_hello",
                    Message = "Expected a hello message.",
                }, ct);
                await client.CloseAsync("handshake timeout", ct);
                return;
            }

            var hello = ProtocolJson.Deserialize(helloJson);
            if (hello is null || hello.Type != MessageTypes.Hello)
            {
                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.Error, Code = "bad_hello", Message = "Invalid hello message.",
                }, ct);
                await client.CloseAsync("bad hello", ct);
                return;
            }

            if (hello.ProtocolVersion != BuildInfo.ProtocolVersion)
            {
                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.Error,
                    Code = "protocol_mismatch",
                    Message = $"PC speaks protocol v{BuildInfo.ProtocolVersion}; update the app.",
                }, ct);
                await client.CloseAsync("protocol mismatch", ct);
                return;
            }

            var device = _pairing.FindByToken(hello.Token);
            var newlyPaired = false;
            if (device is null && !string.IsNullOrWhiteSpace(hello.Code))
            {
                device = _pairing.PairWithCode(hello.Code, hello.DeviceName, out var pairError);
                if (device is null)
                {
                    await client.SendAsync(new WireMessage
                    {
                        Type = MessageTypes.Error, Code = "pair_failed", Message = pairError,
                    }, ct);
                    await client.CloseAsync("pairing failed", ct);
                    return;
                }
                newlyPaired = true;
            }

            if (device is null)
            {
                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.Error,
                    Code = "unauthorized",
                    Message = "Unknown device. Pair with the code shown on the PC.",
                }, ct);
                await client.CloseAsync("unauthorized", ct);
                return;
            }

            _pairing.Touch(device);
            client.Authenticate(device.Name);
            _clients[client.Id] = client;
            RaiseStatus();

            var snapshot = SnapshotProvider?.Invoke();
            await client.SendAsync(new WireMessage
            {
                Type = MessageTypes.HelloAck,
                ProtocolVersion = BuildInfo.ProtocolVersion,
                ServerVersion = BuildInfo.Version,
                PcName = _config.Current.PcName,
                PcId = _config.Current.PcId,
                Token = newlyPaired ? device.Token : null,
                Games = _config.Current.Games.Select(g => g.ToDto()).ToList(),
                Session = snapshot?.Session,
            }, ct);

            if (snapshot?.Prompt is { } prompt)
                await client.SendAsync(new WireMessage { Type = MessageTypes.Prompt, Prompt = prompt }, ct);
            if (snapshot?.Session is { } session)
                await client.SendAsync(new WireMessage { Type = MessageTypes.SessionStarted, Session = session }, ct);
            if (snapshot?.LastSample is { } last)
                await client.SendAsync(new WireMessage { Type = MessageTypes.Sample, Sample = last }, ct);

            Log.Info($"Phone connected over Wi-Fi: {device.Name}.");
            await ReceiveLoopAsync(client, ct);
        }
        catch (WebSocketException ex)
        {
            Log.Debug($"WebSocket closed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Warn($"WebSocket connection error: {ex.Message}");
        }
        finally
        {
            context.Response.Abort();
        }
    }

    private async Task ReceiveLoopAsync(WsClient client, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && client.IsOpen)
            {
                var json = await ReceiveTextAsync(client.Socket, ct);
                if (json is null) break;

                var message = ProtocolJson.Deserialize(json);
                if (message is null)
                {
                    await client.SendAsync(new WireMessage
                    {
                        Type = MessageTypes.Error, Code = "bad_json", Message = "Could not parse message.",
                    }, ct);
                    continue;
                }

                await DispatchAsync(message, client, ct);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (WebSocketException) { /* phone went away */ }
        catch (Exception ex)
        {
            Log.Debug($"WebSocket receive loop ended: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            RaiseStatus();
            Log.Info($"Phone disconnected: {client.DeviceName ?? "unknown"}.");
        }
    }

    private async Task DispatchAsync(WireMessage message, ITransportClient client, CancellationToken ct)
    {
        switch (message.Type)
        {
            case MessageTypes.Ping:
                await client.SendAsync(new WireMessage { Type = MessageTypes.Pong }, ct);
                return;

            case MessageTypes.ListSessions:
                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.Sessions,
                    Sessions = _store.GetAll(),
                }, ct);
                return;

            case MessageTypes.GetSession:
            {
                var summary = message.SessionId is null ? null : _store.Get(message.SessionId);
                if (summary is null)
                {
                    await client.SendAsync(new WireMessage
                    {
                        Type = MessageTypes.Error, Code = "not_found", Message = "Session not found.",
                    }, ct);
                    return;
                }

                IReadOnlyList<TelemetrySample> samples;
                try { samples = SessionRecorder.ReadSamples(summary.Id, 1500); }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read samples for {summary.Id}: {ex.Message}");
                    samples = [];
                }

                await client.SendAsync(new WireMessage
                {
                    Type = MessageTypes.SessionDetail,
                    Summary = summary,
                    Samples = samples.ToList(),
                }, ct);
                return;
            }
        }

        if (CommandHandler is null)
        {
            await client.SendAsync(new WireMessage
            {
                Type = MessageTypes.Error, Code = "unavailable", Message = "Session controller is not ready.",
            }, ct);
            return;
        }

        await CommandHandler(message, client);
    }

    public async Task BroadcastAsync(WireMessage message)
    {
        foreach (var client in _clients.Values)
        {
            try { await client.SendAsync(message); }
            catch (Exception ex) { Log.Debug($"Broadcast failed: {ex.Message}"); }
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > 64 * 1024) throw new InvalidOperationException("WebSocket message too large.");
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void RaiseStatus()
    {
        try { StatusChanged?.Invoke(); } catch { /* ignore */ }
    }

    public async Task StopAsync()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _listener?.Close(); } catch { /* ignore */ }
        _listener = null;

        foreach (var client in _clients.Values)
        {
            try { await client.CloseAsync("server stopping", CancellationToken.None); }
            catch { /* ignore */ }
        }
        _clients.Clear();

        if (_acceptTask is not null)
        {
            try { await Task.WhenAny(_acceptTask, Task.Delay(1000)); } catch { /* ignore */ }
            _acceptTask = null;
        }
        _cts?.Dispose();
        _cts = null;
        RaiseStatus();
        Log.Info("WebSocket server stopped.");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class WsClient : ITransportClient
    {
        private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly SemaphoreSlim _closeGate = new(1, 1);

        public WsClient(WebSocket socket)
        {
            Socket = socket;
            Id = Guid.NewGuid().ToString("N");
        }

        public WebSocket Socket { get; }
        public string Id { get; }
        public string? DeviceName { get; private set; }
        public string TransportName => "Wi-Fi";
        public bool IsAuthenticated => DeviceName is not null;
        public bool IsOpen => Socket.State == WebSocketState.Open;

        public void Authenticate(string deviceName) => DeviceName = deviceName;

        public void StartSender(CancellationToken ct)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var json in _outbox.Reader.ReadAllAsync(ct))
                    {
                        if (Socket.State != WebSocketState.Open) break;
                        var bytes = Encoding.UTF8.GetBytes(json);
                        await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                    }
                }
                catch (OperationCanceledException) { /* expected */ }
                catch (Exception ex) { Log.Debug($"WebSocket send loop ended: {ex.Message}"); }
            }, ct);
        }

        public Task SendAsync(WireMessage message, CancellationToken ct = default)
        {
            if (!IsOpen) return Task.CompletedTask;
            var json = ProtocolJson.Serialize(message);
            return _outbox.Writer.TryWrite(json) ? Task.CompletedTask : Task.CompletedTask;
        }

        public async Task CloseAsync(string reason, CancellationToken ct)
        {
            await _closeGate.WaitAsync(ct);
            try
            {
                _outbox.Writer.TryComplete();
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct);
                }
                Socket.Dispose();
            }
            catch { /* ignore */ }
            finally
            {
                _closeGate.Release();
            }
        }
    }
}
