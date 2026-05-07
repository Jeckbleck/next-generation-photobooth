using System.Windows;
using Photobooth.Camera;
using Photobooth.Services;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        public static CameraService    Camera   { get; } = new CameraService();
        public static SettingsManager  Settings { get; } = new SettingsManager();

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);

            Log.Information("App startup — initializing camera");
            Camera.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Disposing camera service");
            Camera.Dispose();
            AppLogger.Shutdown();
            base.OnExit(e);
        }
    }
}
