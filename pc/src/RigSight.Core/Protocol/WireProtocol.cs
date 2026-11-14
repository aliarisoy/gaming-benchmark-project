using System.Text.Json;
using System.Text.Json.Serialization;

namespace RigSight.Core.Protocol;

/// <summary>
/// A deliberately flat wire envelope. Both transports speak the same vocabulary;
/// absent fields are omitted by <see cref="ProtocolJson.Options"/>.
/// </summary>
public sealed class WireMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("protocolVersion")] public int? ProtocolVersion { get; set; }
    [JsonPropertyName("serverVersion")] public string? ServerVersion { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("deviceName")] public string? DeviceName { get; set; }
    [JsonPropertyName("appVersion")] public string? AppVersion { get; set; }

    [JsonPropertyName("pcName")] public string? PcName { get; set; }
    [JsonPropertyName("pcId")] public string? PcId { get; set; }
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("requiresPairing")] public bool? RequiresPairing { get; set; }
    [JsonPropertyName("wsPort")] public int? WsPort { get; set; }
    [JsonPropertyName("wsHosts")] public List<string>? WsHosts { get; set; }

    [JsonPropertyName("prompt")] public Models.PromptInfo? Prompt { get; set; }
    [JsonPropertyName("session")] public Models.SessionInfo? Session { get; set; }
    [JsonPropertyName("sample")] public Models.TelemetrySample? Sample { get; set; }
    [JsonPropertyName("summary")] public Models.SessionSummary? Summary { get; set; }
    [JsonPropertyName("sessions")] public List<Models.SessionSummary>? Sessions { get; set; }
    [JsonPropertyName("samples")] public List<Models.TelemetrySample>? Samples { get; set; }
    [JsonPropertyName("games")] public List<Models.GameDto>? Games { get; set; }

    [JsonPropertyName("promptId")] public string? PromptId { get; set; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
    [JsonPropertyName("gameId")] public string? GameId { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public static class MessageTypes
{
    public const string Hello = "hello";
    public const string HelloAck = "hello_ack";
    public const string BleHello = "ble_hello";
    public const string BleHelloAck = "ble_hello_ack";
    public const string DeviceInfo = "device_info";
    public const string Prompt = "prompt";
    public const string PromptClosed = "prompt_closed";
    public const string SessionStarted = "session_started";
    public const string Sample = "sample";
    public const string SessionEnded = "session_ended";
    public const string Sessions = "sessions";
    public const string SessionDetail = "session_detail";
    public const string StartSession = "start_session";
    public const string DeclinePrompt = "decline_prompt";
    public const string StopSession = "stop_session";
    public const string ListSessions = "list_sessions";
    public const string GetSession = "get_session";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Serialize(WireMessage message) => JsonSerializer.Serialize(message, Options);

    public static WireMessage? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WireMessage>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class RigSightUuids
{
    public const string Service = "8f1e0001-5c3a-4f8e-9b7d-2a6c1f4e9a10";
    public const string ControlCharacteristic = "8f1e0002-5c3a-4f8e-9b7d-2a6c1f4e9a10";
    public const string EventCharacteristic = "8f1e0003-5c3a-4f8e-9b7d-2a6c1f4e9a10";

    public static readonly Guid ServiceGuid = Guid.Parse(Service);
    public static readonly Guid ControlGuid = Guid.Parse(ControlCharacteristic);
    public static readonly Guid EventGuid = Guid.Parse(EventCharacteristic);

    /// <summary>First byte of every BLE fragment. 'R' for RigSight.</summary>
    public const byte BleFrameMagic = 0x52;
    public const byte BleFrameVersion = 0x01;
    public const int BleHeaderSize = 6;
}
