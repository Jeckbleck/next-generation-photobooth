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

        public (int Sessions, int Photos, int Prints, int AIGenerations) GetStats(int eventId) =>
            _repo.GetStats(eventId);

        public int GetEventPrintCount(int eventId) => _repo.CountPrints(eventId);

        public int GetSessionPrintCount(int sessionId) => _repo.GetPrintCount(sessionId);

        // --- Mutations -----------------------------------------------------------

        public Event Create(string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimitPerEvent, int? printLimitPerSession)
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
                PrintLimitPerEvent   = printLimitPerEvent,
                PrintLimitPerSession = printLimitPerSession,
            };

            _repo.Add(ev);
            _repo.SaveChanges();
            _fileStorage.CreateEventFolders(ev.Slug);
            Log.Information("Created event '{Name}' (slug: {Slug})", ev.Name, ev.Slug);
            return ev;
        }

        public void UpdateDetails(int id, string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimitPerEvent, int? printLimitPerSession)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Event name cannot be empty.", nameof(name));

            var ev = Require(id);
            ev.Name                 = name.Trim();
            ev.PaywallEnabled       = paywallEnabled;
            ev.SaveImagesEnabled    = saveImagesEnabled;
            ev.PrintLimitPerEvent   = printLimitPerEvent;
            ev.PrintLimitPerSession = printLimitPerSession;

            _repo.SaveChanges();
            Log.Information("Updated event '{Name}'", ev.Name);
        }

        public void SetGreetingText(int id, string? eyebrow, string? title, string? subtitle)
        {
            var ev = Require(id);
            ev.GreetingEyebrow  = string.IsNullOrWhiteSpace(eyebrow)  ? null : eyebrow.Trim();
            ev.GreetingTitle    = string.IsNullOrWhiteSpace(title)    ? null : title.Trim();
            ev.GreetingSubtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim();
            _repo.SaveChanges();
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

        public void SetEventPrintLimit(int id, int? limit)
        {
            Require(id).PrintLimitPerEvent = limit;
            _repo.SaveChanges();
            Log.Debug("Event {Id} event print limit → {Value}", id, limit?.ToString() ?? "unlimited");
        }

        public void SetSessionPrintLimit(int id, int? limit)
        {
            Require(id).PrintLimitPerSession = limit;
            _repo.SaveChanges();
            Log.Debug("Event {Id} session print limit → {Value}", id, limit?.ToString() ?? "unlimited");
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

        public void SetNavColor(int id, string? color)
        {
            Require(id);
            _repo.SetNavColor(id, color);
            _repo.SaveChanges();
            Log.Debug("Event {Id} nav color → {Value}", id, color ?? "default");
        }

        public void SetBackgroundImagePath(int id, string? path)
        {
            Require(id);
            _repo.SetBackgroundImagePath(id, path);
            _repo.SaveChanges();
            Log.Debug("Event {Id} background image → {Value}", id, path ?? "none");
        }

        public void SetPhotostripTemplatePath(int id, string? path)
        {
            Require(id);
            _repo.SetPhotostripTemplatePath(id, path);
            _repo.SaveChanges();
            Log.Debug("Event {Id} photostrip template → {Value}", id, path ?? "none");
        }

        public void RecordPrint(int sessionId, int copies = 1)
        {
            var session = _repo.FindSessionById(sessionId)
                ?? throw new InvalidOperationException($"Session {sessionId} not found.");

            var ev = Require(session.EventId);

            // Check event-level total print cap (aggregate across all sessions).
            // CountPrints always hits the DB — bypasses the EF Core identity-map cache.
            if (ev.PrintLimitPerEvent.HasValue)
            {
                var eventTotal = _repo.CountPrints(session.EventId);
                if (eventTotal + copies > ev.PrintLimitPerEvent.Value)
                    throw new InvalidOperationException(
                        $"Event print limit of {ev.PrintLimitPerEvent.Value} reached.");
            }

            // Check per-session cap — prevents a single group printing excessively.
            // GetPrintCount is also a projection query, always fresh from the DB.
            if (ev.PrintLimitPerSession.HasValue)
            {
                var sessionTotal = _repo.GetPrintCount(sessionId);
                if (sessionTotal + copies > ev.PrintLimitPerSession.Value)
                    throw new InvalidOperationException(
                        $"Session print limit of {ev.PrintLimitPerSession.Value} reached.");
            }

            // AddPrints uses ExecuteUpdate (atomic SQL increment), no SaveChanges needed.
            _repo.AddPrints(sessionId, copies);
            Log.Information("Session {SessionId} printed {Copies} cop(ies)", sessionId, copies);
        }

        // --- Session / photo lifecycle -------------------------------------------

        public Session StartSession(int eventId)
        {
            Require(eventId);
            var session = _repo.AddSession(eventId);
            Log.Information("Started session {SessionId} for event {EventId}", session.Id, eventId);
            return session;
        }

        public void AbandonSession(int sessionId)
        {
            _repo.DeleteSession(sessionId);
            Log.Information("Abandoned empty session {SessionId}", sessionId);
        }

        public void RecordPhoto(int sessionId, int sequence, string filePath)
        {
            _repo.AddPhoto(sessionId, sequence, filePath);
            _repo.SaveChanges();
            Log.Debug("Recorded photo {Sequence} for session {SessionId}: {Path}", sequence, sessionId, filePath);
        }

        public void RecordEnhancedVariant(int sessionId, int sequence, string styleId, string styleName, string filePath)
        {
            _repo.AddOrUpdateEnhancedVariant(sessionId, sequence, styleId, styleName, filePath);
            Log.Debug("Enhanced variant {Style} photo {Seq} session {Sid}: {Path}", styleId, sequence, sessionId, filePath);
        }

        public List<Photo> GetRecentPhotos(int eventId, int count = 9) =>
            _repo.GetRecentPhotos(eventId, count);

        public List<Session> GetSessionsWithPhotos(int eventId) =>
            _repo.GetSessionsWithPhotos(eventId);

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
