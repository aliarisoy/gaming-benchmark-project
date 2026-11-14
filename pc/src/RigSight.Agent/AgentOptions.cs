namespace RigSight.Agent;

public sealed record AgentOptions(bool Demo, bool StartMinimized, bool NoBle, int? Port)
{
    public static AgentOptions Parse(string[] args)
    {
        var demo = false;
        var minimized = false;
        var noBle = false;
        int? port = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--demo":
                    demo = true;
                    break;
                case "--minimized":
                case "--start-minimized":
                    minimized = true;
                    break;
                case "--no-ble":
                    noBle = true;
                    break;
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var value))
                    {
                        port = value;
                        i++;
                    }
                    break;
                default:
                    if (args[i].StartsWith("--port=", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(args[i][7..], out var inlinePort))
                        port = inlinePort;
                    break;
            }
        }

        return new AgentOptions(demo, minimized, noBle, port);
    }
}
