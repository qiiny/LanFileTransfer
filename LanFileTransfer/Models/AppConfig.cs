using System.IO;
using System.Text.Json;

namespace LanFileTransfer.Models;

public class AppConfig
{
    public int Port { get; set; } = 8888;
    public string DownloadPath { get; set; } = string.Empty;

    private static string ConfigFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanFileTransfer",
            "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
        }
        catch { }

        return new AppConfig
        {
            DownloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "LanFileTransfer")
        };
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch { }
    }
}