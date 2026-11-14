using System.Text;
using RigSight.Core.Protocol;

namespace RigSight.Core.Tests;

public sealed class BleFrameCodecTests
{
    [Fact]
    public void Encode_FragmentsAndReassembles_OutOfOrder()
    {
        var json = "{\"type\":\"sample\",\"payload\":\"" + new string('x', 120) + "\"}";
        var frames = BleFrameCodec.Encode(json, maxNotificationSize: 23, messageId: 42);

        Assert.True(frames.Count > 1);
        Assert.All(frames, frame => Assert.InRange(frame.Length, RigSightUuids.BleHeaderSize, 23));

        var reassembler = new BleFrameCodec.Reassembler();
        string? result = null;
        foreach (var frame in frames.AsEnumerable().Reverse())
        {
            var current = reassembler.Add(frame);
            if (current is not null) result = current;
        }

        Assert.Equal(json, result);
    }

    [Fact]
    public void Encode_SingleFrame_RoundTrips()
    {
        const string json = "{\"ok\":true}";
        var frame = Assert.Single(BleFrameCodec.Encode(json, maxNotificationSize: 40, messageId: 7));

        Assert.Equal(RigSightUuids.BleFrameMagic, frame[0]);
        Assert.Equal(RigSightUuids.BleFrameVersion, frame[1]);
        Assert.Equal(0, frame[4]);
        Assert.Equal(1, frame[5]);

        var result = new BleFrameCodec.Reassembler().Add(frame);
        Assert.Equal(json, result);
    }

    [Fact]
    public void RawJson_IsAcceptedWithoutFraming()
    {
        var result = new BleFrameCodec.Reassembler().Add(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"));
        Assert.Equal("{\"type\":\"ping\"}", result);
    }

    [Fact]
    public void DuplicateFragment_DoesNotCorruptMessage()
    {
        var json = "{\"type\":\"sample\",\"payload\":\"" + new string('y', 80) + "\"}";
        var frames = BleFrameCodec.Encode(json, maxNotificationSize: 23, messageId: 9);
        var reassembler = new BleFrameCodec.Reassembler();

        Assert.Null(reassembler.Add(frames[0]));
        Assert.Null(reassembler.Add(frames[0])); // retransmit
        string? result = null;
        for (var i = 1; i < frames.Count; i++) result = reassembler.Add(frames[i]) ?? result;

        Assert.Equal(json, result);
    }

    [Fact]
    public void InterleavedMessageIds_DoNotMix()
    {
        var firstJson = "{\"message\":\"first\",\"payload\":\"" + new string('a', 50) + "\"}";
        var secondJson = "{\"message\":\"second\",\"payload\":\"" + new string('b', 50) + "\"}";
        var first = BleFrameCodec.Encode(firstJson, 23, 100);
        var second = BleFrameCodec.Encode(secondJson, 23, 101);
        var reassembler = new BleFrameCodec.Reassembler();

        string? firstResult = null;
        string? secondResult = null;
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count) firstResult = reassembler.Add(first[i]) ?? firstResult;
            if (i < second.Count) secondResult = reassembler.Add(second[i]) ?? secondResult;
        }

        Assert.Equal(firstJson, firstResult);
        Assert.Equal(secondJson, secondResult);
    }

    [Fact]
    public void InvalidFrames_AreIgnored()
    {
        var reassembler = new BleFrameCodec.Reassembler();
        Assert.Null(reassembler.Add([]));
        Assert.Null(reassembler.Add([RigSightUuids.BleFrameMagic, 0x7F, 0, 1, 0, 1]));
        Assert.Null(reassembler.Add([RigSightUuids.BleFrameMagic, RigSightUuids.BleFrameVersion, 0, 1, 2, 2]));
        Assert.Null(reassembler.Add([RigSightUuids.BleFrameMagic, RigSightUuids.BleFrameVersion]));
    }

    [Fact]
    public void OversizedMessage_Throws()
    {
        var json = new string('z', 6000);
        Assert.Throws<InvalidOperationException>(() => BleFrameCodec.Encode(json, maxNotificationSize: 23));
    }
}
