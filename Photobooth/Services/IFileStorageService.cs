namespace Photobooth.Services
{
    /// <summary>
    /// Owns all file-system path logic and folder lifecycle for events.
    /// No business rules live here — only path construction and I/O.
    /// </summary>
    public interface IFileStorageService
    {
        /// <summary>The root directory under which all event folders are stored.</summary>
        string StorageRoot { get; }

        /// <summary>
        /// Creates the standard sub-folder structure for a newly created event:
        /// <code>
        /// {StorageRoot}/{slug}/
        ///   Photos/
        ///   Backgrounds/
        ///   Strip template/
        /// </code>
        /// </summary>
        void CreateEventFolders(string slug);

        /// <summary>Returns the Photos directory for the given event slug.</summary>
        string GetPhotosPath(string slug);

        /// <summary>
        /// Returns the date-bucketed directory for a session's photos.
        /// Creates the directory if it does not already exist.
        /// </summary>
        string GetSessionPhotosPath(string slug, DateTime date);

        /// <summary>Returns the Backgrounds directory for the given event slug.</summary>
        string GetBackgroundsPath(string slug);

        /// <summary>Returns the Strip template directory for the given event slug.</summary>
        string GetStripTemplatePath(string slug);
    }
}
