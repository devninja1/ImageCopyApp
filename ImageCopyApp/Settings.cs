using System;
using System.IO;
using System.Text.Json;

namespace ImageCopyApp;

public class AppSettings
{
    public string? LowResFolder { get; set; }
    public string? HiResFolder { get; set; }
    public string? DestinationFolder { get; set; }
    public bool Overwrite { get; set; } = false;
    public bool MatchByNameOnly { get; set; } = true;

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ImageCopyApp", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { /* ignore */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore */ }
    }
}