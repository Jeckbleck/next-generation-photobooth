using System.Windows;
using Photobooth.Camera;
using Photobooth.Print;
using Photobooth.Settings;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        public static CameraService  Camera   { get; } = new CameraService();
        public static SettingsManager Settings { get; } = new SettingsManager();
        public static PrintService   Printer  { get; } = new PrintService(Settings);

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);
            Settings.Load();
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
