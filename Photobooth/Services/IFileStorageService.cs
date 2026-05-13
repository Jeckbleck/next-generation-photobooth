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

        /// <summary>Returns the Animated directory for the given event slug.</summary>
        string GetAnimatedPath(string slug);

        /// <summary>Returns the Enhanced directory for the given event slug.</summary>
        string GetEnhancedPath(string slug);

        /// <summary>Returns the Backgrounds directory for the given event slug.</summary>
        string GetBackgroundsPath(string slug);

        /// <summary>Returns the Strip template directory for the given event slug.</summary>
        string GetStripTemplatePath(string slug);
    }
}
