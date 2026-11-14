using System.Text;

namespace RigSight.Core.Protocol;

/// <summary>
/// Fragments a JSON message into BLE-sized frames and reassembles them.
/// Frame: 'R' | version | messageId u16 BE | index | count | payload.
/// </summary>
public static class BleFrameCodec
{
    public static List<byte[]> Encode(string json, int maxNotificationSize, ushort? messageId = null)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var mtu = Math.Max(23, maxNotificationSize);
        var maxPayload = Math.Max(1, mtu - RigSightUuids.BleHeaderSize);
        var count = Math.Max(1, (payload.Length + maxPayload - 1) / maxPayload);
        if (count > 255) throw new InvalidOperationException("BLE message is too large to fragment.");

        var id = messageId ?? (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var frames = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = index * maxPayload;
            var length = Math.Min(maxPayload, payload.Length - offset);
            var frame = new byte[RigSightUuids.BleHeaderSize + length];
            frame[0] = RigSightUuids.BleFrameMagic;
            frame[1] = RigSightUuids.BleFrameVersion;
            frame[2] = (byte)(id >> 8);
            frame[3] = (byte)(id & 0xFF);
            frame[4] = (byte)index;
            frame[5] = (byte)count;
            Buffer.BlockCopy(payload, offset, frame, RigSightUuids.BleHeaderSize, length);
            frames.Add(frame);
        }
        return frames;
    }

    public sealed class Reassembler
    {
        private readonly Dictionary<ushort, Partial> _parts = new();
        private readonly object _gate = new();

        private sealed class Partial
        {
            public int Count;
            public int Received;
            public byte[]?[] Chunks = [];
            public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
        }

        /// <summary>Returns a complete JSON string, or null while more fragments are needed.</summary>
        public string? Add(byte[] data)
        {
            if (data.Length == 0) return null;

            // Backward/forgiving path: a raw JSON write.
            if (data[0] != RigSightUuids.BleFrameMagic)
                return Encoding.UTF8.GetString(data);

            if (data.Length < RigSightUuids.BleHeaderSize) return null;
            if (data[1] != RigSightUuids.BleFrameVersion) return null;

            var id = (ushort)((data[2] << 8) | data[3]);
            var index = data[4];
            var count = data[5];
            if (count == 0 || index >= count) return null;

            lock (_gate)
            {
                Prune();
                if (!_parts.TryGetValue(id, out var part))
                {
                    part = new Partial { Count = count, Chunks = new byte[count][] };
                    _parts[id] = part;
                }

                if (part.Count != count) return null;
                if (part.Chunks[index] is null)
                {
                    part.Chunks[index] = data[RigSightUuids.BleHeaderSize..];
                    part.Received++;
                }

                if (part.Received < part.Count) return null;

                _parts.Remove(id);
                var total = part.Chunks.Sum(c => c?.Length ?? 0);
                var buffer = new byte[total];
                var offset = 0;
                foreach (var chunk in part.Chunks)
                {
                    if (chunk is null) return null;
                    Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
                    offset += chunk.Length;
                }
                return Encoding.UTF8.GetString(buffer);
            }
        }

        private void Prune()
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5);
            foreach (var key in _parts.Where(p => p.Value.CreatedAt < cutoff).Select(p => p.Key).ToArray())
                _parts.Remove(key);
        }
    }
}
