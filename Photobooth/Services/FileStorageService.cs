using System.IO;
using Serilog;

namespace Photobooth.Services
{
    /// <summary>
    /// Concrete implementation of IFileStorageService.
    /// Reads the storage root from SettingsManager so path changes
    /// take effect immediately without restarting the app.
    /// </summary>
    public class FileStorageService : IFileStorageService
    {
        private readonly SettingsManager _settings;

        public FileStorageService(SettingsManager settings) => _settings = settings;

        public string StorageRoot => _settings.StorageRoot;

        public void CreateEventFolders(string slug)
        {
            Directory.CreateDirectory(GetPhotosPath(slug));
            Directory.CreateDirectory(GetBackgroundsPath(slug));
            Directory.CreateDirectory(GetStripTemplatePath(slug));
            Log.Information("Created folder structure for '{Slug}' under {Root}", slug, StorageRoot);
        }

        public string GetPhotosPath(string slug) =>
            Path.Combine(StorageRoot, slug, "Photos");

        public string GetSessionPhotosPath(string slug, DateTime date)
        {
            var path = Path.Combine(GetPhotosPath(slug), date.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(path);
            return path;
        }

        public string GetBackgroundsPath(string slug) =>
            Path.Combine(StorageRoot, slug, "Backgrounds");

        public string GetStripTemplatePath(string slug) =>
            Path.Combine(StorageRoot, slug, "Strip template");
    }
}
