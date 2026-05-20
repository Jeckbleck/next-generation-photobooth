namespace Photobooth.Data.Models
{
    public class EnhancedVariant
    {
        public int      Id        { get; set; }
        public int      PhotoId   { get; set; }
        public string   StyleId   { get; set; } = "";
        public string   StyleName { get; set; } = "";
        public string   FilePath  { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Photo Photo { get; set; } = null!;
    }
}
