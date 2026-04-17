using System.Windows;
using Photobooth.Camera;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        public static CameraService Camera { get; } = new CameraService();

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);
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
