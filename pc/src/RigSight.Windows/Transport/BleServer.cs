using System.Text;
using System.Text.Json;
using RigSight.Core.Logging;
using RigSight.Core.Models;
using RigSight.Core.Protocol;
using RigSight.Core.Storage;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace RigSight.Windows.Transport;

/// <summary>
/// Windows-side BLE peripheral. It advertises the RigSight service, authenticates a
/// phone with the six-digit code, and carries prompts/control plus 1 Hz sample
/// fallback when Wi-Fi is unavailable.
/// </summary>
public sealed class BleServer : IAsyncDisposable
{
    private readonly ConfigStore _config;
    private readonly PairingCoordinator _pairing;
    private readonly BleFrameCodec.Reassembler _reassembler = new();
    private readonly SemaphoreSlim _notifyGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _stateGate = new();
    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _eventCharacteristic;
    private GattLocalCharacteristic? _controlCharacteristic;
    private bool _authenticated;
    private PairedDevice? _pairedDevice;
    private string? _deviceName;
    private DateTimeOffset _lastSampleSent = DateTimeOffset.MinValue;

    public BleServer(ConfigStore config, PairingCoordinator pairing)
    {
        _config = config;
        _pairing = pairing;
    }

    public bool IsRunning { get; private set; }
    public bool AdvertiseWiFi { get; set; } = true;
    public string? LastError { get; private set; }
    public int ClientCount => _eventCharacteristic?.SubscribedClients.Count ?? 0;
    public IReadOnlyList<string> Clients => _authenticated && _deviceName is not null ? [_deviceName] : [];
    public string TransportName => "Bluetooth";

    public Func<WireMessage, ITransportClient?, Task>? CommandHandler { get; set; }
    public Func<TransportSnapshot>? SnapshotProvider { get; set; }
    public event Action? StatusChanged;

    public async Task StartAsync()
    {
        if (IsRunning) return;
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
            {
                LastError = "No Bluetooth adapter found.";
                Log.Warn(LastError);
                return;
            }
            if (!adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported)
            {
                LastError = "This Bluetooth adapter does not support the LE peripheral role. Wi-Fi still works.";
                Log.Warn(LastError);
                return;
            }

            var serviceResult = await GattServiceProvider.CreateAsync(RigSightUuids.ServiceGuid);
            if (serviceResult.Error != BluetoothError.Success)
            {
                LastError = $"Could not create BLE service: {serviceResult.Error}.";
                Log.Warn(LastError);
                return;
            }

            _provider = serviceResult.ServiceProvider;
            _eventCharacteristic = await CreateCharacteristicAsync(
                RigSightUuids.EventGuid,
                GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                "RigSight events");
            _controlCharacteristic = await CreateCharacteristicAsync(
                RigSightUuids.ControlGuid,
                GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                "RigSight control");

            _eventCharacteristic.ReadRequested += OnReadRequested;
            _eventCharacteristic.SubscribedClientsChanged += OnSubscribedClientsChanged;
            _controlCharacteristic.WriteRequested += OnWriteRequested;

            var parameters = new GattServiceProviderAdvertisingParameters
            {
                IsDiscoverable = true,
                IsConnectable = true,
            };
            _provider.StartAdvertising(parameters);
            IsRunning = true;
            LastError = null;
            Log.Info("BLE GATT server advertising as RigSight peripheral.");
            RaiseStatus();
        }
        catch (Exception ex)
        {
            LastError = $"BLE server failed: {ex.Message}";
            Log.Error(LastError);
            IsRunning = false;
        }
    }

    private async Task<GattLocalCharacteristic> CreateCharacteristicAsync(
        Guid uuid,
        GattCharacteristicProperties properties,
        string description)
    {
        if (_provider is null) throw new InvalidOperationException("GATT provider not created.");
        var parameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = properties,
            UserDescription = description,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            WriteProtectionLevel = GattProtectionLevel.Plain,
        };
        var result = await _provider.Service.CreateCharacteristicAsync(uuid, parameters);
        if (result.Error != BluetoothError.Success)
            throw new InvalidOperationException($"Characteristic {uuid} failed: {result.Error}.");
        return result.Characteristic;
    }

    private void OnReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        // WinRT deferrals must be acquired synchronously on the event thread.
        var deferral = args.GetDeferral();
        _ = Task.Run(async () =>
        {
            try
            {
                var request = await args.GetRequestAsync();
                if (request is null) return;
                var info = new WireMessage
                {
                    Type = MessageTypes.DeviceInfo,
                    PcName = _config.Current.PcName,
                    PcId = _config.Current.PcId,
                    ProtocolVersion = BuildInfo.ProtocolVersion,
                    RequiresPairing = _config.Current.PairedDevices.Count == 0,
                    WsPort = _config.Current.WsPort,
                };
                var json = ProtocolJson.Serialize(info);
                request.RespondWithValue(ToBuffer(Encoding.UTF8.GetBytes(json)));
            }
            catch (Exception ex)
            {
                Log.Warn($"BLE read request failed: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void OnSubscribedClientsChanged(GattLocalCharacteristic sender, object args)
    {
        var count = sender.SubscribedClients.Count;
        Log.Info($"BLE subscribers changed: {count}.");
        if (count == 0)
        {
            lock (_stateGate)
            {
                _authenticated = false;
                _pairedDevice = null;
                _deviceName = null;
            }
        }
        RaiseStatus();
    }

    private void OnWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        // Acquire the deferral before returning from the event; taking it inside
        // Task.Run throws "A method was called at an unexpected time" on WinRT.
        var deferral = args.GetDeferral();
        _ = Task.Run(async () =>
        {
            GattWriteRequest? request = null;
            try
            {
                await _writeGate.WaitAsync();
                try
                {
                    request = await args.GetRequestAsync();
                    if (request is null) return;
                    var bytes = ReadBuffer(request.Value);
                    var json = _reassembler.Add(bytes);
                    if (json is not null)
                    {
                        var message = ProtocolJson.Deserialize(json);
                        if (message is null)
                        {
                            await NotifyAllAsync(new WireMessage
                            {
                                Type = MessageTypes.Error, Code = "bad_json", Message = "Invalid BLE message.",
                            });
                        }
                        else
                        {
                            await HandleMessageAsync(message);
                        }
                    }

                    if (request.Option == GattWriteOption.WriteWithResponse)
                        request.Respond();
                }
                finally
                {
                    _writeGate.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"BLE write failed: {ex.Message}");
                try { request?.Respond(); } catch { /* ignore */ }
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private async Task HandleMessageAsync(WireMessage message)
    {
        if (message.Type == MessageTypes.BleHello)
            {
                await HandleHelloAsync(message);
                return;
            }

            if (!_authenticated)
            {
                await NotifyAllAsync(new WireMessage
                {
                    Type = MessageTypes.Error,
                    Code = "unauthorized",
                    Message = "Pair with the code shown on the PC first.",
                });
                return;
            }

            if (message.Type == MessageTypes.Ping)
            {
                await NotifyAllAsync(new WireMessage { Type = MessageTypes.Pong });
                return;
            }

            if (CommandHandler is not null)
                await CommandHandler(message, new BleTransportClient(this));
        }

    private async Task HandleHelloAsync(WireMessage hello)
    {
        PairedDevice? device = null;
        string? error = null;
        var newlyPaired = false;

        device = _pairing.FindByToken(hello.Token);
        if (device is null && !string.IsNullOrWhiteSpace(hello.Code))
        {
            device = _pairing.PairWithCode(hello.Code, hello.DeviceName, out error);
            newlyPaired = device is not null;
        }
        else if (device is null)
        {
            error = "Pairing code or token required.";
        }

        if (device is null)
        {
            await NotifyAllAsync(new WireMessage
            {
                Type = MessageTypes.BleHelloAck,
                Ok = false,
                Reason = error,
                PcName = _config.Current.PcName,
            });
            return;
        }

        _pairing.Touch(device);
        lock (_stateGate)
        {
            _authenticated = true;
            _pairedDevice = device;
            _deviceName = device.Name;
        }

        await NotifyAllAsync(new WireMessage
        {
            Type = MessageTypes.BleHelloAck,
            Ok = true,
            PcName = _config.Current.PcName,
            PcId = _config.Current.PcId,
            ProtocolVersion = BuildInfo.ProtocolVersion,
            ServerVersion = BuildInfo.Version,
            Token = newlyPaired ? device.Token : null,
            WsPort = AdvertiseWiFi ? _config.Current.WsPort : null,
            WsHosts = AdvertiseWiFi ? NetworkHelper.LanAddresses() : [],
        });

        var snapshot = SnapshotProvider?.Invoke();
        if (snapshot?.Prompt is { } prompt)
            await NotifyAllAsync(new WireMessage { Type = MessageTypes.Prompt, Prompt = prompt });
        if (snapshot?.Session is { } session)
            await NotifyAllAsync(new WireMessage { Type = MessageTypes.SessionStarted, Session = session });
        if (snapshot?.LastSample is { } last)
            await NotifyAllAsync(new WireMessage { Type = MessageTypes.Sample, Sample = last });

        RaiseStatus();
    }

    public async Task SendAsync(WireMessage message)
    {
        if (!IsRunning || _eventCharacteristic is null) return;
        if (message.Type == MessageTypes.Sample)
        {
            // BLE fallback is deliberately low-rate; Wi-Fi carries the full feed.
            if (DateTimeOffset.UtcNow - _lastSampleSent < TimeSpan.FromSeconds(1)) return;
            _lastSampleSent = DateTimeOffset.UtcNow;
        }

        await NotifyAllAsync(message);
    }

    private async Task NotifyAllAsync(WireMessage message)
    {
        if (_eventCharacteristic is null) return;
        var clients = _eventCharacteristic.SubscribedClients.ToArray();
        if (clients.Length == 0) return;

        var json = ProtocolJson.Serialize(message);
        await _notifyGate.WaitAsync();
        try
        {
            foreach (var client in clients)
            {
                var max = Math.Max(23, (int)client.MaxNotificationSize);
                var fragments = BleFrameCodec.Encode(json, max);
                foreach (var fragment in fragments)
                {
                    try
                    {
                        await _eventCharacteristic.NotifyValueAsync(ToBuffer(fragment), client);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"BLE notify failed: {ex.Message}");
                        break;
                    }

                    if (fragments.Count > 1) await Task.Delay(8);
                }
            }
        }
        finally
        {
            _notifyGate.Release();
        }
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static IBuffer ToBuffer(byte[] bytes)
    {
        var writer = new DataWriter();
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }

    private void RaiseStatus()
    {
        try { StatusChanged?.Invoke(); } catch { /* ignore */ }
    }

    public Task StopAsync()
    {
        try { _provider?.StopAdvertising(); } catch { /* ignore */ }
        IsRunning = false;
        lock (_stateGate)
        {
            _authenticated = false;
            _pairedDevice = null;
            _deviceName = null;
        }
        RaiseStatus();
        Log.Info("BLE server stopped.");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _notifyGate.Dispose();
        _writeGate.Dispose();
    }

    private sealed class BleTransportClient : ITransportClient
    {
        private readonly BleServer _server;
        public BleTransportClient(BleServer server) => _server = server;
        public string Id => "ble";
        public string? DeviceName => _server._deviceName;
        public string TransportName => "Bluetooth";
        public Task SendAsync(WireMessage message, CancellationToken ct = default)
            => _server.SendAsync(message);
    }
}
