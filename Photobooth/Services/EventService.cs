using System.Text.RegularExpressions;
using Photobooth.Data.Models;
using Photobooth.Data.Repositories;
using Serilog;

namespace Photobooth.Services
{
    /// <summary>
    /// Business-layer implementation of IEventService.
    /// Enforces rules (validation, slug generation) and delegates all
    /// persistence to IEventRepository — it never touches DbContext directly.
    /// </summary>
    public class EventService : IEventService
    {
        private readonly IEventRepository    _repo;
        private readonly IFileStorageService _fileStorage;

        public EventService(IEventRepository repo, IFileStorageService fileStorage)
        {
            _repo        = repo;
            _fileStorage = fileStorage;
        }

        // --- Queries -------------------------------------------------------------

        public List<Event> GetActive() => _repo.GetActive();

        public Event? GetById(int id) => _repo.FindById(id);

        public (int Sessions, int Photos) GetStats(int eventId) =>
            (_repo.CountSessions(eventId), _repo.CountPhotos(eventId));

        // --- Mutations -----------------------------------------------------------

        public Event Create(string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimit)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Event name cannot be empty.", nameof(name));

            if (string.IsNullOrWhiteSpace(_fileStorage.StorageRoot))
                throw new InvalidOperationException("A storage root path must be configured before creating events.");

            var ev = new Event
            {
                Name                 = name.Trim(),
                Slug                 = GenerateSlug(name),
                PaywallEnabled       = paywallEnabled,
                SaveImagesEnabled    = saveImagesEnabled,
                PrintLimitPerSession = printLimit,
            };

            _repo.Add(ev);
            _repo.SaveChanges();
            _fileStorage.CreateEventFolders(ev.Slug);
            Log.Information("Created event '{Name}' (slug: {Slug})", ev.Name, ev.Slug);
            return ev;
        }

        public void UpdateDetails(int id, string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimit)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Event name cannot be empty.", nameof(name));

            var ev = Require(id);
            ev.Name                 = name.Trim();
            ev.PaywallEnabled       = paywallEnabled;
            ev.SaveImagesEnabled    = saveImagesEnabled;
            ev.PrintLimitPerSession = printLimit;

            _repo.SaveChanges();
            Log.Information("Updated event '{Name}'", ev.Name);
        }

        public void SetPaywall(int id, bool enabled)
        {
            Require(id).PaywallEnabled = enabled;
            _repo.SaveChanges();
            Log.Debug("Event {Id} paywall → {Value}", id, enabled);
        }

        public void SetSaveImages(int id, bool enabled)
        {
            Require(id).SaveImagesEnabled = enabled;
            _repo.SaveChanges();
            Log.Debug("Event {Id} save images → {Value}", id, enabled);
        }

        public void SetPrintLimit(int id, int? limit)
        {
            Require(id).PrintLimitPerSession = limit;
            _repo.SaveChanges();
            Log.Debug("Event {Id} print limit → {Value}", id, limit?.ToString() ?? "unlimited");
        }

        public void ClearSessions(int id)
        {
            _repo.RemoveSessions(id);
            _repo.SaveChanges();
            Log.Information("Cleared sessions for event {Id}", id);
        }

        public void Archive(int id)
        {
            var ev = Require(id);
            _repo.Archive(id);
            _repo.SaveChanges();
            Log.Information("Archived event '{Name}' (id: {Id})", ev.Name, id);
        }

        // --- Appearance ----------------------------------------------------------

        public void SetAccentColor(int id, string? color)
        {
            Require(id);
            _repo.SetAccentColor(id, color);
            _repo.SaveChanges();
            Log.Debug("Event {Id} accent color → {Value}", id, color ?? "default");
        }

        public void SetBackgroundColor(int id, string? color)
        {
            Require(id);
            _repo.SetBackgroundColor(id, color);
            _repo.SaveChanges();
            Log.Debug("Event {Id} background color → {Value}", id, color ?? "default");
        }

        public void SetSurfaceColor(int id, string? color)
        {
            Require(id);
            _repo.SetSurfaceColor(id, color);
            _repo.SaveChanges();
            Log.Debug("Event {Id} surface color → {Value}", id, color ?? "default");
        }

        public void SetBackgroundImagePath(int id, string? path)
        {
            Require(id);
            _repo.SetBackgroundImagePath(id, path);
            _repo.SaveChanges();
            Log.Debug("Event {Id} background image → {Value}", id, path ?? "none");
        }

        public void RecordPrint(int sessionId, int copies = 1)
        {
            var session = _repo.FindSessionById(sessionId)
                ?? throw new InvalidOperationException($"Session {sessionId} not found.");

            var ev = Require(session.EventId);
            if (ev.PrintLimitPerSession.HasValue &&
                session.PrintCount + copies > ev.PrintLimitPerSession.Value)
            {
                throw new InvalidOperationException(
                    $"Print limit of {ev.PrintLimitPerSession.Value} reached for this session.");
            }

            _repo.AddPrints(sessionId, copies);
            _repo.SaveChanges();
            Log.Information("Session {SessionId} printed {Copies} cop(ies) — total: {Total}",
                sessionId, copies, session.PrintCount + copies);
        }

        // --- Private helpers -----------------------------------------------------

        private Event Require(int id) =>
            _repo.FindById(id) ?? throw new InvalidOperationException($"Event {id} not found.");

        /// <summary>
        /// Derives a URL-safe slug from a display name.
        /// Example: "Summer Bash 2026" → "summer-bash-2026-2026"
        /// </summary>
        public static string GenerateSlug(string name)
        {
            var slug = name.ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\s]", "");
            slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
            return $"{slug}-{DateTime.UtcNow.Year}";
        }
    }
}
