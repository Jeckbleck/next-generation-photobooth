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

        public Event? FindById(int id) => _db.Events.Find(id);

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

        public void SaveChanges() => _db.SaveChanges();
    }
}
