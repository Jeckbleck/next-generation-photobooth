namespace Photobooth.Data.Models
{
    public class StripTemplateConfig
    {
        public List<StripSlotDefinition>   Slots           { get; set; } = new();
        public string?                     BackgroundColor { get; set; }
        public List<TextElementDefinition> TextElements    { get; set; } = new();
    }

    public class StripSlotDefinition
    {
        public int    Index    { get; set; }  // 1-based, matches photo capture order
        public double X        { get; set; }  // normalised 0–1 relative to canvas width
        public double Y        { get; set; }  // normalised 0–1 relative to canvas height
        public double Width    { get; set; }
        public double Height   { get; set; }
        public int    Rotation { get; set; }  // clockwise degrees: 0, 90, 180, 270
    }

    public class TextElementDefinition
    {
        public string Content  { get; set; } = string.Empty;
        public double X        { get; set; }  // normalised 0–1, same convention as StripSlotDefinition
        public double Y        { get; set; }
        public double Width    { get; set; }
        public double Height   { get; set; }
        public string Color    { get; set; } = "#FFFFFF";
        public double FontSize { get; set; } = 24;
    }
}
