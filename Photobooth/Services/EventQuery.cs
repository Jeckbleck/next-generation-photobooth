namespace Photobooth.Services
{
    public enum EventSortOrder
    {
        NewestFirst,
        OldestFirst,
        NameAZ,
        NameZA
    }

    public sealed class EventQuery
    {
        public string?        Search          { get; set; }
        public EventSortOrder Sort            { get; set; } = EventSortOrder.NewestFirst;
        public bool           IncludeArchived { get; set; } = false;
        public int            Page            { get; set; } = 1;
        public int            PageSize        { get; set; } = 9;
    }
}
