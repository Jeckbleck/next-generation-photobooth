using System;
using System.IO;
using Serilog;

namespace Photobooth
{
    internal static class AppLogger
    {
        public static void Initialize()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "Logs");

            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(logDir, "photobooth-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== Photobooth starting up ===");
            Log.Information("Log directory: {LogDir}", logDir);
        }

        public static void Shutdown()
        {
            Log.Information("=== Photobooth shutting down ===");
            Log.CloseAndFlush();
        }
    }
}
