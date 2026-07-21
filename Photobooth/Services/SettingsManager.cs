using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Photobooth.Services
{
    public class SettingsManager
    {
        private static readonly string DefaultFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Photobooth", "settings.json");

        public static readonly string DefaultStorageRoot = Path.Combine("C:\\", "Photobooth");

        private readonly string _filePath;
        private AppSettings _settings;

        public SettingsManager() : this(DefaultFilePath) { }

        public SettingsManager(string filePath)
        {
            _filePath = filePath;
            _settings = Load();
        }

        public AppSettings Settings => _settings;

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

        public string? PrinterName => _settings.PrinterName;

        public void SetPrinterName(string? name)
        {
            _settings.PrinterName = name;
            Save();
            Log.Information("Printer name updated to {Name}", name ?? "auto-detect");
        }

        public bool AutoPrint => _settings.AutoPrint;

        public void SetAutoPrint(bool value)
        {
            _settings.AutoPrint = value;
            Save();
            Log.Debug("Auto-print → {Value}", value);
        }

        public string BrandingText => _settings.BrandingText;

        public void SetBrandingText(string text)
        {
            _settings.BrandingText = text;
            Save();
        }

        public int CountdownSeconds    => _settings.CountdownSeconds;
        public int PreviewHoldSeconds  => _settings.PreviewHoldSeconds;

        public void SetCountdownSeconds(int value)
        {
            _settings.CountdownSeconds = Math.Clamp(value, 3, 10);
            Save();
        }

        public void SetPreviewHoldSeconds(int value)
        {
            _settings.PreviewHoldSeconds = Math.Clamp(value, 1, 10);
            Save();
        }

        public int RetakeHoldSeconds => _settings.RetakeHoldSeconds;
        public int MaxRetakesPerSlot => _settings.MaxRetakesPerSlot;

        public void SetRetakeHoldSeconds(int value)
        {
            _settings.RetakeHoldSeconds = Math.Clamp(value, 2, 10);
            Save();
        }

        public void SetMaxRetakesPerSlot(int value)
        {
            _settings.MaxRetakesPerSlot = Math.Clamp(value, 0, 10);
            Save();
        }

        public bool ExperimentalFeatures => _settings.ExperimentalFeatures;

        public void SetExperimentalFeatures(bool value)
        {
            _settings.ExperimentalFeatures = value;
            Save();
            Log.Debug("Experimental features → {Value}", value);
        }

        public bool   AIEnhancementEnabled => _settings.AIEnhancementEnabled;
        public string AIServerUrl          => _settings.AIServerUrl;
        public string AIApiKey             => _settings.AIApiKey;

        public void SetAIEnhancementEnabled(bool value)
        {
            _settings.AIEnhancementEnabled = value;
            Save();
            Log.Debug("AI enhancement → {Value}", value);
        }

        public void SetAIServerUrl(string url)
        {
            _settings.AIServerUrl = url;
            Save();
        }

        public void SetAIApiKey(string key)
        {
            _settings.AIApiKey = key;
            Save();
        }

        public void SetStorageRoot(string path)
        {
            _settings.StorageRoot = path;
            Save();
            Log.Information("Storage root updated to {Path}", path);
        }

        public IReadOnlyList<CameraPreset> CameraPresets => _settings.CameraPresets;

        public void SaveCameraPreset(string name, uint iso, uint tv, uint av)
        {
            _settings.CameraPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            _settings.CameraPresets.Add(new CameraPreset { Name = name, Iso = iso, Tv = tv, Av = av });
            Save();
            Log.Information("Camera preset saved: {Name}", name);
        }

        public void DeleteCameraPreset(string name)
        {
            _settings.CameraPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            Save();
            Log.Information("Camera preset deleted: {Name}", name);
        }

        public int CameraRotationDegrees => _settings.CameraRotationDegrees;

        // Snaps to {0,90,180,270} (mod 360). Any value that isn't exactly one of those
        // four after modulo — e.g. 45 — falls back to 0 rather than guessing a "nearest"
        // angle, since only those four values are ever meaningful for this setting.
        public void SetCameraRotationDegrees(int degrees)
        {
            int normalized = ((degrees % 360) + 360) % 360;
            if (normalized is not (0 or 90 or 180 or 270))
                normalized = 0;

            _settings.CameraRotationDegrees = normalized;
            Save();
            Log.Information("Camera rotation set to {Degrees}°", normalized);
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded is not null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.StorageRoot))
                        {
                            loaded.StorageRoot = DefaultStorageRoot;
                            Save(loaded);
                        }
                        Log.Debug("Settings loaded from {Path}", _filePath);
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

        public void Save() => Save(_settings);

        private void Save(AppSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save settings to {Path}", _filePath);
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
        public string  PinHash               { get; set; } = SettingsManager.HashPin("1234");
        public int?    ActiveEventId         { get; set; }
        public string  StorageRoot           { get; set; } = SettingsManager.DefaultStorageRoot;
        public string? PrinterName           { get; set; }
        public bool    AutoPrint             { get; set; } = true;
        public string  BrandingText          { get; set; } = "THE NEXT GENERATION PHOTOBOOTH";
        public int     CountdownSeconds       { get; set; } = 3;
        public int     PreviewHoldSeconds    { get; set; } = 5;
        public int     RetakeHoldSeconds     { get; set; } = 3;
        public int     MaxRetakesPerSlot     { get; set; } = 3;
        public bool    ExperimentalFeatures   { get; set; } = false;
        public bool    AIEnhancementEnabled  { get; set; } = false;
        public string  AIServerUrl           { get; set; } = "http://localhost:8000";
        public string  AIApiKey              { get; set; } = "";
        public List<CameraPreset> CameraPresets { get; set; } = DefaultCameraPresets();
        public int CameraRotationDegrees { get; set; } = 0;

        public static List<CameraPreset> DefaultCameraPresets() => new()
        {
            new CameraPreset { Name = "Outdoor Daylight", Iso = 0x00000048, Tv = 0x00000070, Av = 0x00000030 },
            new CameraPreset { Name = "Indoor",           Iso = 0x00000058, Tv = 0x00000068, Av = 0x00000028 },
            new CameraPreset { Name = "Low Light",        Iso = 0x00000060, Tv = 0x00000068, Av = 0x00000020 },
        };
    }

    public class CameraPreset
    {
        public string Name { get; set; } = "";
        public uint   Iso  { get; set; }
        public uint   Tv   { get; set; }
        public uint   Av   { get; set; }
    }
}
