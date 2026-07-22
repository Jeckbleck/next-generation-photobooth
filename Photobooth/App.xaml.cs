using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Camera;
using Photobooth.Data;
using Photobooth.Data.Repositories;
using Photobooth.Print;
using Photobooth.Services;
using Photobooth.Views;
using Serilog;

namespace Photobooth
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        // async void is the accepted exception to the rule for WPF's OnStartup —
        // it's the top-level entry point, not a method with a caller to signal back to.
        protected override async void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);

            // Shown immediately, before any of the slower startup work below, so the
            // kiosk never shows a blank/absent window while the DB migrates and the
            // camera connects. Uses the settings default until the real SettingsManager
            // is resolved a few lines down (near-instant, so no visible flash in practice).
            var splash = new SplashWindow(new AppSettings().BrandingText);
            splash.Show();

            var sc = new ServiceCollection();

            // Infrastructure
            sc.AddSingleton<SettingsManager>();
            sc.AddSingleton<PhotoboothDbContext>();
            sc.AddSingleton<IEventRepository, EventRepository>();

            // Services
            sc.AddSingleton<CameraService>();
            sc.AddSingleton<IFileStorageService, FileStorageService>();
            sc.AddSingleton<IEventService, EventService>();
            sc.AddSingleton<IPrintAdapter, WindowsPrintAdapter>();
            sc.AddSingleton<PrintService>();
            sc.AddSingleton<AIEnhancementClient>();

            // Flow
            sc.AddSingleton<INavigator, WindowsNavigator>();
            sc.AddSingleton<FlowController>();

            // Pages — transient so each navigation gets a fresh instance
            sc.AddTransient<GreetingPage>();
            sc.AddTransient<ShootPage>();
            sc.AddTransient<StylePickerPage>();
            sc.AddTransient<ResultsPage>();

            Services = sc.BuildServiceProvider();

            var settings = Services.GetRequiredService<SettingsManager>();
            splash.SetBranding(settings.BrandingText);

            Log.Information("Initialising database — applying migrations");
            splash.SetStatus("Loading database…");
            await Task.Run(() => Services.GetRequiredService<PhotoboothDbContext>().Database.Migrate());

            var eventRepo = Services.GetRequiredService<IEventRepository>();
            if (settings.ActiveEventId.HasValue && eventRepo.FindById(settings.ActiveEventId.Value) is null)
            {
                Log.Warning("ActiveEventId {Id} not found in database — clearing stale reference", settings.ActiveEventId.Value);
                settings.SetActiveEventId(null);
            }

            Log.Information("App startup — initializing camera");
            splash.SetStatus("Connecting to camera…");
            var camera = Services.GetRequiredService<CameraService>();
            // Must run on this thread, not Task.Run: EdsInitializeSDK binds the SDK's
            // internal event/callback plumbing to whichever OS thread calls it, and that
            // thread must keep running for the app's lifetime (this one, via the WPF
            // Dispatcher loop, does). A ThreadPool thread is recycled the instant this
            // call returns, permanently orphaning EVF/property/object event delivery —
            // camera.Initialize() ran here silently breaking live view app-wide.
            camera.Initialize();
            camera.RotationDegrees = settings.CameraRotationDegrees;

            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("App shutting down");
            Services.GetRequiredService<CameraService>().Dispose();
            Services.GetRequiredService<PhotoboothDbContext>().Dispose();
            AppLogger.Shutdown();
            base.OnExit(e);
        }
    }
}
