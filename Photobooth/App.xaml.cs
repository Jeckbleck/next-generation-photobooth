using System.Windows;
using Photobooth.Camera;
using Photobooth.Data;
using Photobooth.Services;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        public static CameraService       Camera   { get; } = new CameraService();
        public static SettingsManager     Settings { get; } = new SettingsManager();
        public static PhotoboothDbContext Db       { get; } = new PhotoboothDbContext();

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);

            Log.Information("Initialising database");
            Db.Database.EnsureCreated();

            Log.Information("App startup — initializing camera");
            Camera.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Disposing camera service");
            Camera.Dispose();
            Db.Dispose();
            AppLogger.Shutdown();
            base.OnExit(e);
        }
    }
}
