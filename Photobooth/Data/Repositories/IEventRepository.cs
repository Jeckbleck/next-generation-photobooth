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

        /// <summary>Returns all events including archived ones, newest first.</summary>
        List<Event> GetAllIncludingArchived();

        /// <summary>Returns the event with the given id, or null if not found.</summary>
        Event? FindById(int id);

        /// <summary>Returns the session with the given id, or null if not found.</summary>
        Session? FindSessionById(int id);

        /// <summary>
        /// Returns session, photo, print, and AI-generation counts for the event in a single query.
        /// </summary>
        (int Sessions, int Photos, int Prints, int AIGenerations) GetStats(int eventId);

        /// <summary>Returns the total number of prints across all sessions of the event.</summary>
        int CountPrints(int eventId);

        /// <summary>Returns the current print count for a single session, always reading from the database.</summary>
        int GetPrintCount(int sessionId);

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

        // --- Appearance ----------------------------------------------------------

        /// <summary>Sets the accent colour override for the event. Null clears the override.</summary>
        void SetAccentColor(int eventId, string? color);

        /// <summary>Sets the background colour override for the event. Null clears the override.</summary>
        void SetBackgroundColor(int eventId, string? color);

        /// <summary>Sets the surface colour override for the event. Null clears the override.</summary>
        void SetSurfaceColor(int eventId, string? color);

        /// <summary>Sets the navigation/deep-background colour override for the event. Null clears the override.</summary>
        void SetNavColor(int eventId, string? color);

        /// <summary>Sets the primary text colour override for the event. Null clears the override.</summary>
        void SetTextColor(int eventId, string? color);

        /// <summary>Sets the secondary text colour (eyebrow + subtitle) override for the event. Null clears the override.</summary>
        void SetTextSecondaryColor(int eventId, string? color);

        /// <summary>Sets the background image path for the event. Null removes it.</summary>
        void SetBackgroundImagePath(int eventId, string? path);

        /// <summary>Sets the photostrip template image path for the event. Null removes it.</summary>
        void SetPhotostripTemplatePath(int eventId, string? path);

        /// <summary>Increments the print counter for a session by the given number of copies.</summary>
        void AddPrints(int sessionId, int copies);

        // --- Session / photo lifecycle -------------------------------------------

        /// <summary>Creates a new session for the given event, saves, and returns it.</summary>
        Session AddSession(int eventId);

        /// <summary>Removes a session (and its photos via cascade) and saves. No-op if not found.</summary>
        void DeleteSession(int sessionId);

        /// <summary>Stages a new photo record for the session. Caller must call SaveChanges.</summary>
        void AddPhoto(int sessionId, int sequence, string filePath);

        /// <summary>
        /// Inserts or updates the EnhancedVariant for (photoId, styleId) and marks Photo.IsEnhanced.
        /// Saves immediately.
        /// </summary>
        void AddOrUpdateEnhancedVariant(int sessionId, int sequence, string styleId, string styleName, string filePath);

        /// <summary>Returns the <paramref name="count"/> most recent photos for the event, newest session first.</summary>
        List<Photo> GetRecentPhotos(int eventId, int count);

        /// <summary>Returns all sessions for the event with photos and enhanced variants eagerly loaded, newest first.</summary>
        List<Session> GetSessionsWithPhotos(int eventId);

        /// <summary>Persists all pending changes to the database.</summary>
        void SaveChanges();
    }
}
