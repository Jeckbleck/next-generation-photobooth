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

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Initialize();
            base.OnStartup(e);

            var sc = new ServiceCollection();

            // Infrastructure
            sc.AddSingleton<SettingsManager>();
            sc.AddSingleton<PhotoboothDbContext>();
            sc.AddSingleton<IEventRepository, EventRepository>();

            // Services
            sc.AddSingleton<CameraService>();
            sc.AddSingleton<IFileStorageService, FileStorageService>();
            sc.AddSingleton<IEventService, EventService>();
            sc.AddSingleton<PrintService>();
            sc.AddSingleton<AIEnhancementClient>();

            // Flow
            sc.AddSingleton<FlowController>();

            // Pages — transient so each navigation gets a fresh instance
            sc.AddTransient<GreetingPage>();
            sc.AddTransient<ShootPage>();
            sc.AddTransient<StylePickerPage>();
            sc.AddTransient<ResultsPage>();

            Services = sc.BuildServiceProvider();

            Log.Information("Initialising database — applying migrations");
            Services.GetRequiredService<PhotoboothDbContext>().Database.Migrate();

            var settings    = Services.GetRequiredService<SettingsManager>();
            var eventRepo   = Services.GetRequiredService<IEventRepository>();

            if (settings.ActiveEventId.HasValue && eventRepo.FindById(settings.ActiveEventId.Value) is null)
            {
                Log.Warning("ActiveEventId {Id} not found in database — clearing stale reference", settings.ActiveEventId.Value);
                settings.SetActiveEventId(null);
            }

            Log.Information("App startup — initializing camera");
            Services.GetRequiredService<CameraService>().Initialize();
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
