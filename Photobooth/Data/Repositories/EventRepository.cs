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

        public List<Event> GetAllIncludingArchived() =>
            _db.Events.IgnoreQueryFilters().OrderByDescending(e => e.CreatedAt).ToList();

        public Event?   FindById(int id)        => _db.Events.Find(id);
        public Session? FindSessionById(int id) => _db.Sessions.Find(id);

        public (int Sessions, int Photos, int Prints, int AIGenerations) GetStats(int eventId)
        {
            // Single round-trip: fetch per-session aggregates as a flat projection,
            // then sum client-side. EF Core translates Photos.Count() and
            // Photos.SelectMany(EnhancedVariants).Count() as correlated subqueries.
            var rows = _db.Sessions
                .Where(s => s.EventId == eventId)
                .Select(s => new
                {
                    s.PrintCount,
                    Photos        = s.Photos.Count(),
                    AIGenerations = s.Photos.SelectMany(p => p.EnhancedVariants).Count()
                })
                .ToList();

            return (
                rows.Count,
                rows.Sum(r => r.Photos),
                rows.Sum(r => r.PrintCount),
                rows.Sum(r => r.AIGenerations)
            );
        }

        public int CountPrints(int eventId) =>
            _db.Sessions.Where(s => s.EventId == eventId).Sum(s => (int?)s.PrintCount) ?? 0;

        // Projection query — always hits the DB, bypasses the change-tracker cache.
        public int GetPrintCount(int sessionId) =>
            _db.Sessions.Where(s => s.Id == sessionId).Select(s => s.PrintCount).FirstOrDefault();

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

        public void SetNavColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.NavColor = color;
        }

        public void SetTextColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.TextColor = color;
        }

        public void SetTextSecondaryColor(int eventId, string? color)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.TextSecondaryColor = color;
        }

        public void SetBackgroundImagePath(int eventId, string? path)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.BackgroundImagePath = path;
        }

        public void SetPhotostripTemplatePath(int eventId, string? path)
        {
            var ev = _db.Events.Find(eventId);
            if (ev is not null) ev.PhotostripTemplatePath = path;
        }

        public void AddPrints(int sessionId, int copies)
        {
            // ExecuteUpdate issues "UPDATE … SET PrintCount = PrintCount + @copies" directly —
            // atomic, bypasses the change-tracker, so stale cached values can never corrupt the count.
            _db.Sessions
               .Where(s => s.Id == sessionId)
               .ExecuteUpdate(s => s.SetProperty(p => p.PrintCount, p => p.PrintCount + copies));
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

        public void AddOrUpdateEnhancedVariant(int sessionId, int sequence, string styleId, string styleName, string filePath)
        {
            var photo = _db.Photos
                .Include(p => p.EnhancedVariants)
                .FirstOrDefault(p => p.SessionId == sessionId && p.Sequence == sequence);
            if (photo is null) return;

            var existing = photo.EnhancedVariants.FirstOrDefault(v => v.StyleId == styleId);
            if (existing is not null)
            {
                existing.FilePath   = filePath;
                existing.StyleName  = styleName;
                existing.CreatedAt  = DateTime.UtcNow;
            }
            else
            {
                photo.EnhancedVariants.Add(new EnhancedVariant
                {
                    StyleId   = styleId,
                    StyleName = styleName,
                    FilePath  = filePath,
                });
            }

            photo.IsEnhanced = true;
            _db.SaveChanges();
        }

        public List<Photo> GetRecentPhotos(int eventId, int count) =>
            _db.Photos
               .Where(p => p.Session.EventId == eventId && p.FilePath != null)
               .OrderByDescending(p => p.Session.CreatedAt)
               .ThenBy(p => p.Sequence)
               .Take(count)
               .ToList();

        public List<Session> GetSessionsWithPhotos(int eventId) =>
            _db.Sessions
               .AsNoTracking()
               .Include(s => s.Photos)
                   .ThenInclude(p => p.EnhancedVariants)
               .Where(s => s.EventId == eventId)
               .OrderByDescending(s => s.CreatedAt)
               .ToList();

        public void SaveChanges() => _db.SaveChanges();
    }
}
