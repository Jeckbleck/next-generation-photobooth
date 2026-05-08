using Photobooth.Data.Models;

namespace Photobooth.Data.Repositories
{
    /// <summary>
    /// Data-layer contract for event and session persistence.
    /// Implementations must not contain business logic — only data access.
    /// </summary>
    public interface IEventRepository
    {
        // --- Queries -------------------------------------------------------------

        /// <summary>Returns all non-archived events, newest first.</summary>
        List<Event> GetActive();

        /// <summary>Returns the event with the given id, or null if not found.</summary>
        Event? FindById(int id);

        /// <summary>Returns the number of sessions belonging to the event.</summary>
        int CountSessions(int eventId);

        /// <summary>Returns the total number of photos across all sessions of the event.</summary>
        int CountPhotos(int eventId);

        // --- Mutations -----------------------------------------------------------

        /// <summary>Adds a new event to the tracking context (call SaveChanges to persist).</summary>
        void Add(Event ev);

        /// <summary>
        /// Deletes all sessions (and their photos via cascade) for the given event.
        /// Does not call SaveChanges.
        /// </summary>
        void RemoveSessions(int eventId);

        /// <summary>
        /// Sets ArchivedAt to the current UTC time, soft-deleting the event.
        /// Does not call SaveChanges.
        /// </summary>
        void Archive(int eventId);

        /// <summary>Persists all pending changes to the database.</summary>
        void SaveChanges();
    }
}
