using Photobooth.Data.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Business-layer contract for event management.
    /// The presentation layer must depend only on this interface, never on the
    /// repository or DbContext directly.
    /// </summary>
    public interface IEventService
    {
        // --- Queries -------------------------------------------------------------

        /// <summary>Returns all active (non-archived) events, newest first.</summary>
        List<Event> GetActive();

        /// <summary>Returns the event with the given id, or null if not found.</summary>
        Event? GetById(int id);

        /// <summary>Returns session and photo counts for the given event.</summary>
        (int Sessions, int Photos) GetStats(int eventId);

        // --- Mutations -----------------------------------------------------------

        /// <summary>
        /// Creates and persists a new event.
        /// Throws <see cref="ArgumentException"/> if name is blank.
        /// </summary>
        Event Create(string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimit);

        /// <summary>Updates the core details of an existing event and persists.</summary>
        void UpdateDetails(int id, string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimit);

        /// <summary>Toggles the paywall flag and persists immediately.</summary>
        void SetPaywall(int id, bool enabled);

        /// <summary>Toggles the save-images flag and persists immediately.</summary>
        void SetSaveImages(int id, bool enabled);

        /// <summary>Updates the print limit (null = unlimited) and persists immediately.</summary>
        void SetPrintLimit(int id, int? limit);

        /// <summary>Deletes all sessions (and their photos) for the given event.</summary>
        void ClearSessions(int id);

        /// <summary>
        /// Soft-deletes the event by setting ArchivedAt.
        /// Archived events are excluded from all future queries.
        /// </summary>
        void Archive(int id);

        // --- Appearance ----------------------------------------------------------

        void SetAccentColor(int id, string? color);
        void SetBackgroundColor(int id, string? color);
        void SetSurfaceColor(int id, string? color);
        void SetBackgroundImagePath(int id, string? path);
        void SetPhotostripTemplatePath(int id, string? path);

        /// <summary>
        /// Records that <paramref name="copies"/> prints were produced for a session.
        /// Throws <see cref="InvalidOperationException"/> if the session's event has a
        /// print limit and this call would exceed it.
        /// </summary>
        void RecordPrint(int sessionId, int copies = 1);

        // --- Session / photo lifecycle -------------------------------------------

        /// <summary>Creates and persists a new session for the given event.</summary>
        Session StartSession(int eventId);

        /// <summary>Deletes the session if it exists. Used to clean up abandoned (zero-photo) sessions.</summary>
        void AbandonSession(int sessionId);

        /// <summary>Records a captured photo path for the session and persists.</summary>
        void RecordPhoto(int sessionId, int sequence, string filePath);

        /// <summary>Returns the <paramref name="count"/> most recent photos for the event, newest first.</summary>
        List<Photo> GetRecentPhotos(int eventId, int count = 9);
    }
}
