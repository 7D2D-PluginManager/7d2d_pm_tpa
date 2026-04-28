using System.IO;

namespace TpaPlugin;

public class PluginConfig
{
    public int CooldownSeconds { get; set; } = 60;

    public static PluginConfig Load(string path)
    {
        var config = new PluginConfig();
        if (!File.Exists(path)) return config;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;

            var key = trimmed.Substring(0, idx).Trim();
            var value = trimmed.Substring(idx + 1).Trim();

            if (!int.TryParse(value, out var parsed)) continue;

            switch (key)
            {
                case "cooldown_seconds":
                    config.CooldownSeconds = parsed;
                    break;
            }
        }

        return config;
    }
}
