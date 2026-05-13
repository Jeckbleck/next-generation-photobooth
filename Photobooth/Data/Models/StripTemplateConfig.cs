namespace Photobooth.Data.Models
{
    public class StripTemplateConfig
    {
        public List<StripSlotDefinition> Slots { get; set; } = new();
    }

    public class StripSlotDefinition
    {
        public int    Index  { get; set; }  // 1-based, matches photo capture order
        public double X      { get; set; }  // normalised 0–1 relative to canvas width
        public double Y      { get; set; }  // normalised 0–1 relative to canvas height
        public double Width  { get; set; }
        public double Height { get; set; }
    }
}
