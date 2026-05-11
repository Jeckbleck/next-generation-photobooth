using System.IO;
using System.Text.Json;
using Serilog;

namespace Photobooth.Settings
{
    public sealed class AppSettings
    {
        public string? PrinterName { get; set; }
        public string BrandingText { get; set; } = "THE NEXT GENERATION PHOTOBOOTH";
    }

    public sealed class SettingsManager
    {
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Photobooth", "settings.json");

        public AppSettings Current { get; private set; } = new();

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                Log.Information("Settings loaded from {Path}", _path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings — using defaults");
                Current = new();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path,
                    JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                Log.Information("Settings saved to {Path}", _path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
            }
        }
    }
}
