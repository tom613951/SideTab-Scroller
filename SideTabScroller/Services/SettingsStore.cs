using System.IO;
using System.Text;
using System.Text.Json;
using SideTabScroller.Models;

namespace SideTabScroller.Services;

internal sealed class SettingsStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _ioLock = new();

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SideTabScroller");

    public string ConfigPath => Path.Combine(ConfigDirectory, "settings.json");

    public ScrollerSettings Load()
    {
        lock (_ioLock)
        {
            Directory.CreateDirectory(ConfigDirectory);

            if (!File.Exists(ConfigPath))
            {
                var defaults = ScrollerSettings.CreateDefault();
                Save(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath, Utf8NoBom);
                var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ScrollerSettings)
                    ?? ScrollerSettings.CreateDefault();
                settings.Normalize();
                return settings;
            }
            catch
            {
                var defaults = ScrollerSettings.CreateDefault();
                Save(defaults);
                return defaults;
            }
        }
    }

    public void Save(ScrollerSettings settings)
    {
        lock (_ioLock)
        {
            Directory.CreateDirectory(ConfigDirectory);
            settings.Normalize();

            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.ScrollerSettings);
            var tempPath = ConfigPath + ".tmp";

            try
            {
                File.WriteAllText(tempPath, json, Utf8NoBom);
                File.Move(tempPath, ConfigPath, overwrite: true);
            }
            catch
            {
                File.WriteAllText(ConfigPath, json, Utf8NoBom);
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore cleanup failure
                }
            }
        }
    }
}
