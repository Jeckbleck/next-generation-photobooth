namespace Photobooth.Data.Models
{
    public class Photo
    {
        public int     Id               { get; set; }
        public int     SessionId        { get; set; }
        public int     Sequence         { get; set; }  // 1, 2, or 3

        public string? FilePath         { get; set; }
        public string? EnhancedFilePath { get; set; }
        public bool    IsEnhanced       { get; set; } = false;

        public Session Session { get; set; } = null!;
    }
}
