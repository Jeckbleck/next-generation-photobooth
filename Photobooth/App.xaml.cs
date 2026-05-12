using System.Windows;
using Photobooth.Camera;
using Photobooth.Print;
using Photobooth.Data;
using Photobooth.Data.Repositories;
using Photobooth.Services;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        // --- Internal infrastructure (not exposed to the presentation layer) -----

        private static readonly PhotoboothDbContext _db        = new PhotoboothDbContext();
        private static readonly IEventRepository    _eventRepo = new EventRepository(_db);

        // --- Public API — one entry point per layer boundary --------------------

        public static CameraService       Camera      { get; } = new CameraService();
        public static SettingsManager     Settings    { get; } = new SettingsManager();
        public static IFileStorageService FileStorage { get; } = new FileStorageService(Settings);
        public static IEventService       Events      { get; } = new EventService(_eventRepo, FileStorage);
        public static PrintService        Printer     { get; } = new PrintService(Settings);

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);

            Log.Information("Initialising database");
            _db.Database.EnsureCreated();

            Log.Information("App startup — initializing camera");
            Camera.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("App shutting down");
            Camera.Dispose();
            _db.Dispose();
            AppLogger.Shutdown();
            base.OnExit(e);
        }
    }
}
