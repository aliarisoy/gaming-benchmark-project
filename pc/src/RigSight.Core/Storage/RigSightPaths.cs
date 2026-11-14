namespace RigSight.Core.Storage;

public static class RigSightPaths
{
    public static string Root { get; } = ResolveRoot();
    public static string SessionsDirectory => Path.Combine(Root, "sessions");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string DatabaseFile => Path.Combine(Root, "sessions.db");

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("RIGSIGHT_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigSight")
            : overridden!;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        return root;
    }
}
