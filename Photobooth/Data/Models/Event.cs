using System.ComponentModel.DataAnnotations;

namespace Photobooth.Data.Models
{
    public class Event
    {
        public int      Id                      { get; set; }

        [Required, MaxLength(100)]
        public string   Slug                    { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string   Name                    { get; set; } = string.Empty;

        public DateTime CreatedAt               { get; set; } = DateTime.UtcNow;
        public DateTime? ArchivedAt             { get; set; }

        public bool     PaywallEnabled          { get; set; } = false;
        public bool     SaveImagesEnabled       { get; set; } = true;
        public bool     AutoDeleteAfterSession  { get; set; } = false;
        public int      PrintLimitPerSession    { get; set; } = 1;

        [MaxLength(20)]
        public string?  AccentColor            { get; set; }

        [MaxLength(20)]
        public string?  BackgroundColor        { get; set; }

        [MaxLength(20)]
        public string?  SurfaceColor           { get; set; }

        public string?  BackgroundImagePath    { get; set; }
        public string?  PhotostripTemplatePath { get; set; }

        public ICollection<Session> Sessions   { get; set; } = new List<Session>();
    }
}
