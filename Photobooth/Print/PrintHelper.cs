using System.Drawing;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Data.Models;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Print
{
    public enum PrintOutcome { Success, LimitReached, Failed }

    public readonly record struct PrintResult(PrintOutcome Outcome, string Message)
    {
        public bool CanRetry => Outcome == PrintOutcome.Failed;
    }

    /// <summary>
    /// Shared print pipeline — template resolution, strip composition, and spooling.
    /// Call from any page; no page-specific logic lives here.
    /// </summary>
    public static class PrintHelper
    {
        // --- Template config -----------------------------------------------------

        /// <summary>
        /// Reads the event's photostrip template path and slot definitions.
        /// Returns (null, empty) if the event has no template or the files are missing.
        /// </summary>
        public static (string? templatePath, List<StripSlotDefinition> slots) LoadTemplateConfig(int eventId)
        {
            var events = App.Services.GetRequiredService<IEventService>();
            var ev = events.GetById(eventId);
            if (ev is null) return (null, new());

            var templatePath = ev.PhotostripTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                return (null, new());

            var fileStorage = App.Services.GetRequiredService<IFileStorageService>();
            var jsonPath = Path.Combine(fileStorage.GetStripTemplatePath(ev.Slug), "template.json");
            if (!File.Exists(jsonPath)) return (null, new());

            try
            {
                var config = JsonSerializer.Deserialize<StripTemplateConfig>(File.ReadAllText(jsonPath));
                if (config is null || config.Slots.Count == 0) return (null, new());
                return (templatePath, config.Slots);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read strip template config for event {Id}", eventId);
                return (null, new());
            }
        }

        // --- Composition ---------------------------------------------------------

        /// <summary>
        /// Composes the strip bitmap on a background thread.
        /// Uses the event's custom template when available; falls back to the default layout.
        /// The caller is responsible for disposing the returned <see cref="Bitmap"/>.
        /// </summary>
        public static Task<Bitmap> ComposeStripAsync(int eventId, IReadOnlyList<string> photoPaths)
        {
            (string? templatePath, List<StripSlotDefinition> slots) = LoadTemplateConfig(eventId);
            var branding = App.Services.GetRequiredService<SettingsManager>().BrandingText;

            return Task.Run(() =>
                templatePath is not null && slots.Count > 0
                    ? PhotostripComposer.ComposeFromTemplate(templatePath, slots, photoPaths)
                    : PhotostripComposer.Compose(photoPaths, branding));
        }

        // --- Printing ------------------------------------------------------------

        /// <summary>
        /// Records a print in the DB then spools a pre-composed strip.
        /// The caller retains ownership of <paramref name="strip"/> and must dispose it.
        /// </summary>
        public static async Task<PrintResult> PrintSessionAsync(int sessionId, Bitmap strip)
        {
            if (sessionId > 0)
            {
                try { App.Services.GetRequiredService<IEventService>().RecordPrint(sessionId); }
                catch (InvalidOperationException limitEx)
                {
                    Log.Warning(limitEx, "Print limit reached for session {Id}", sessionId);
                    return new PrintResult(PrintOutcome.LimitReached, "Print limit reached for this session.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not record print for session {Id} — continuing", sessionId);
                }
            }

            try
            {
                await App.Services.GetRequiredService<PrintService>().PrintStripAsync(strip);
                Log.Information("Print job submitted for session {Id}", sessionId);
                return new PrintResult(PrintOutcome.Success, "Your strip is printing!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Print failed for session {Id}", sessionId);
                return new PrintResult(PrintOutcome.Failed, "Print failed — please see staff.");
            }
        }

        /// <summary>
        /// Full pipeline overload: composes the strip from paths then spools it.
        /// Disposes the composed bitmap internally.
        /// </summary>
        public static async Task<PrintResult> PrintSessionAsync(
            int sessionId, int eventId, IReadOnlyList<string> photoPaths)
        {
            Bitmap? strip = null;
            try
            {
                strip = await ComposeStripAsync(eventId, photoPaths);
                return await PrintSessionAsync(sessionId, strip);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not compose strip for session {Id}", sessionId);
                return new PrintResult(PrintOutcome.Failed, "Print failed — please see staff.");
            }
            finally
            {
                strip?.Dispose();
            }
        }
    }
}
