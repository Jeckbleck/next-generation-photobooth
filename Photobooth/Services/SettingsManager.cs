using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Photobooth.Services
{
    public class SettingsManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Photobooth", "settings.json");

        public static readonly string DefaultStorageRoot = Path.Combine("C:\\", "Photobooth");

        private AppSettings _settings;

        public SettingsManager()
        {
            _settings = Load();
        }

        public bool VerifyPin(string pin) => HashPin(pin) == _settings.PinHash;

        public void SetPin(string newPin)
        {
            _settings.PinHash = HashPin(newPin);
            Save();
            Log.Information("Staff PIN updated");
        }

        public int? ActiveEventId => _settings.ActiveEventId;

        public void SetActiveEventId(int? id)
        {
            _settings.ActiveEventId = id;
            Save();
            Log.Debug("Active event set to {Id}", id?.ToString() ?? "none");
        }

        public string StorageRoot => _settings.StorageRoot;

        public bool IsStorageConfigured => !string.IsNullOrWhiteSpace(_settings.StorageRoot);

        public void SetStorageRoot(string path)
        {
            _settings.StorageRoot = path;
            Save();
            Log.Information("Storage root updated to {Path}", path);
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded is not null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.StorageRoot))
                        {
                            loaded.StorageRoot = DefaultStorageRoot;
                            Save(loaded);
                        }
                        Log.Debug("Settings loaded from {Path}", FilePath);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load settings — using defaults");
            }

            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        private void Save() => Save(_settings);

        private static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save settings to {Path}", FilePath);
            }
        }

        public static string HashPin(string pin)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    public class AppSettings
    {
        public string PinHash       { get; set; } = SettingsManager.HashPin("1234");
        public int?   ActiveEventId { get; set; }
        public string StorageRoot   { get; set; } = SettingsManager.DefaultStorageRoot;
    }
}
