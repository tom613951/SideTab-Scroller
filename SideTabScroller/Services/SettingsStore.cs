using System.IO;
using System.Text.Json;
using SideTabScroller.Models;

namespace SideTabScroller.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SideTabScroller");

    public string ConfigPath => Path.Combine(ConfigDirectory, "settings.json");

    public ScrollerSettings Load()
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
            var settings = JsonSerializer.Deserialize<ScrollerSettings>(File.ReadAllText(ConfigPath), JsonOptions)
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

    public void Save(ScrollerSettings settings)
    {
        Directory.CreateDirectory(ConfigDirectory);
        settings.Normalize();
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
