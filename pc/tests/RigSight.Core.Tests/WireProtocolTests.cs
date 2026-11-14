using RigSight.Core.Models;
using RigSight.Core.Protocol;

namespace RigSight.Core.Tests;

public sealed class WireProtocolTests
{
    [Fact]
    public void Serialize_OmitsNullFields()
    {
        var json = ProtocolJson.Serialize(new WireMessage
        {
            Type = MessageTypes.Sample,
            Sample = new TelemetrySample { T = 500, Fps = 120, GpuTempC = 68 },
        });

        Assert.Contains("\"type\":\"sample\"", json);
        Assert.Contains("\"t\":500", json);
        Assert.Contains("\"fps\":120", json);
        Assert.Contains("\"gpuTempC\":68", json);
        Assert.DoesNotContain("cpuTempC", json);
        Assert.DoesNotContain("token", json);
    }

    [Fact]
    public void Deserialize_RoundTripsHello()
    {
        var original = new WireMessage
        {
            Type = MessageTypes.Hello,
            ProtocolVersion = BuildInfo.ProtocolVersion,
            DeviceName = "Test Phone",
            Token = "bearer-token",
            AppVersion = "0.1.0",
        };

        var roundTripped = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(MessageTypes.Hello, roundTripped!.Type);
        Assert.Equal(BuildInfo.ProtocolVersion, roundTripped.ProtocolVersion);
        Assert.Equal("Test Phone", roundTripped.DeviceName);
        Assert.Equal("bearer-token", roundTripped.Token);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownProperties()
    {
        var message = ProtocolJson.Deserialize(
            "{\"type\":\"ping\",\"futureField\":{\"nested\":true},\"sentAt\":12}");

        Assert.NotNull(message);
        Assert.Equal(MessageTypes.Ping, message!.Type);
    }

    [Fact]
    public void Deserialize_ReturnsNullForInvalidJson()
    {
        Assert.Null(ProtocolJson.Deserialize("{not json"));
    }

    [Fact]
    public void PromptAndSession_RoundTrip()
    {
        var prompt = new PromptInfo
        {
            Id = "prompt-1",
            GameId = "game-1",
            GameName = "Test Game",
            ExeName = "test.exe",
            ProcessId = 4242,
            ExpiresAt = 1_700_000_000_000,
        };
        var message = new WireMessage { Type = MessageTypes.Prompt, Prompt = prompt };

        var result = ProtocolJson.Deserialize(ProtocolJson.Serialize(message));

        Assert.Equal("prompt-1", result!.Prompt!.Id);
        Assert.Equal("Test Game", result.Prompt.GameName);
        Assert.Equal(4242, result.Prompt.ProcessId);
        Assert.Equal(1_700_000_000_000, result.Prompt.ExpiresAt);
    }
}
