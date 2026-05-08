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
    }
}
