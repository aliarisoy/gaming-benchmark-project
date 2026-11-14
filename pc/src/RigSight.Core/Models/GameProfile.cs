using System.Text.Json.Serialization;

namespace RigSight.Core.Models;

/// <summary>A game the user has added to their library.</summary>
public sealed class GameProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("exeName")] public string ExeName { get; set; } = "";
    [JsonPropertyName("exePath")] public string? ExePath { get; set; }
    [JsonPropertyName("addedAt")] public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("lastPlayedAt")] public DateTimeOffset? LastPlayedAt { get; set; }
    [JsonPropertyName("notifyPhone")] public bool NotifyPhone { get; set; } = true;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public bool Matches(string processName, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(ExeName)) return false;
        if (!string.Equals(ExeName, processName, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(ExePath) || string.IsNullOrWhiteSpace(processPath)) return true;
        try
        {
            return string.Equals(
                Path.GetFullPath(ExePath),
                Path.GetFullPath(processPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    public GameDto ToDto() => new() { Id = Id, Name = Name, ExeName = ExeName };
}

public sealed class GameDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("exeName")] public string ExeName { get; set; } = "";
}
