using Microsoft.EntityFrameworkCore;
using Photobooth.Data.Models;
using Serilog;

namespace Photobooth.Data.Repositories
{
    /// <summary>
    /// EF Core implementation of IEventRepository.
    /// All DbContext access is confined to this class.
    /// </summary>
    public class EventRepository : IEventRepository
    {
        private readonly PhotoboothDbContext _db;

        public EventRepository(PhotoboothDbContext db) => _db = db;

        // --- Queries -------------------------------------------------------------

        public List<Event> GetActive() =>
            _db.Events.OrderByDescending(e => e.CreatedAt).ToList();

        public Event?   FindById(int id)        => _db.Events.Find(id);
        public Session? FindSessionById(int id) => _db.Sessions.Find(id);

        public int CountSessions(int eventId) =>
            _db.Sessions.Count(s => s.EventId == eventId);

        public int CountPhotos(int eventId) =>
            _db.Photos.Count(p => p.Session.EventId == eventId);

        // --- Mutations -----------------------------------------------------------

        public void Add(Event ev) => _db.Events.Add(ev);

        public void RemoveSessions(int eventId)
        {
            var sessions = _db.Sessions.Where(s => s.EventId == eventId).ToList();
            _db.Sessions.RemoveRange(sessions);
            Log.Debug("Staged removal of {Count} sessions for event {EventId}", sessions.Count, eventId);
        }

        public void Archive(int eventId)
        {
            // IgnoreQueryFilters so we can find already-archived events too (idempotent)
            var ev = _db.Events.IgnoreQueryFilters().FirstOrDefault(e => e.Id == eventId);
            if (ev is not null)
                ev.ArchivedAt = DateTime.UtcNow;
        }

        // --- Appearance ----------------------------------------------------------

        public void SetAccentColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.AccentColor = color;
        }

        public void SetBackgroundColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.BackgroundColor = color;
        }

        public void SetSurfaceColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.SurfaceColor = color;
        }

        public void SetBackgroundImagePath(int eventId, string? path)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.BackgroundImagePath = path;
        }

        public void AddPrints(int sessionId, int copies)
        {
            var session = _db.Sessions.Find(sessionId);
            if (session is not null) session.PrintCount += copies;
        }

        // --- Session / photo lifecycle -------------------------------------------

        public Session AddSession(int eventId)
        {
            var session = new Session { EventId = eventId };
            _db.Sessions.Add(session);
            _db.SaveChanges();
            Log.Debug("Created session {SessionId} for event {EventId}", session.Id, eventId);
            return session;
        }

        public void DeleteSession(int sessionId)
        {
            var session = _db.Sessions.Find(sessionId);
            if (session is null) return;
            _db.Sessions.Remove(session);
            _db.SaveChanges();
            Log.Debug("Deleted session {SessionId}", sessionId);
        }

        public void AddPhoto(int sessionId, int sequence, string filePath)
        {
            _db.Photos.Add(new Photo
            {
                SessionId = sessionId,
                Sequence  = sequence,
                FilePath  = filePath,
            });
        }

        public List<Photo> GetRecentPhotos(int eventId, int count) =>
            _db.Photos
               .Where(p => p.Session.EventId == eventId && p.FilePath != null)
               .OrderByDescending(p => p.Session.CreatedAt)
               .ThenBy(p => p.Sequence)
               .Take(count)
               .ToList();

        public void SaveChanges() => _db.SaveChanges();
    }
}
