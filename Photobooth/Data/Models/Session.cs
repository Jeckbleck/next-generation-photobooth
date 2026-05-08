namespace Photobooth.Data.Models
{
    public class Session
    {
        public int      Id        { get; set; }
        public int      EventId   { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Event              Event  { get; set; } = null!;
        public ICollection<Photo> Photos { get; set; } = new List<Photo>();
    }
}
