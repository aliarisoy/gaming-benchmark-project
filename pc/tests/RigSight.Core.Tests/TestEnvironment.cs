using System.Runtime.CompilerServices;

namespace RigSight.Core.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "RigSightTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("RIGSIGHT_DATA_DIR", root);
    }
}
