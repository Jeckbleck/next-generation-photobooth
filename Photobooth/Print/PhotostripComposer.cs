using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Photobooth.Data.Models;
using Serilog;

namespace Photobooth.Print
{
    public static class PhotostripComposer
    {
        // Portrait 4×6 canvas at 300 dpi.
        // The DNP 2-inch cut bisects the 4" (1240 px) width into two 2×6 portrait strips.
        private const int CanvasW = 1240;
        private const int CanvasH = 1844;
        private const int StripW  = CanvasW / 2; // 620 px = 2 inches
        private const int Padding    = 20;
        private const int BrandingH  = 120;
        private const int PhotoGap   = 12;
        private const int PhotoSlots = 3;

        public static Bitmap Compose(IReadOnlyList<string> photoPaths, string brandingText)
        {
            var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format24bppRgb);
            canvas.SetResolution(300, 300);

            using var g = Graphics.FromImage(canvas);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode     = SmoothingMode.HighQuality;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.Clear(Color.White);

            var images = new List<Image>(PhotoSlots);
            try
            {
                foreach (var path in photoPaths)
                {
                    try   { images.Add(Image.FromFile(path)); }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not load photo for strip: {Path}", path);
                        images.Add(MakePlaceholder());
                    }
                }

                DrawStrip(g, images, 0,      brandingText);
                DrawStrip(g, images, StripW, brandingText);
            }
            finally
            {
                foreach (var img in images) img.Dispose();
            }

            return canvas;
        }

        private static void DrawStrip(Graphics g, List<Image> photos, int xOffset, string brandingText)
        {
            int usableW     = StripW - 2 * Padding;
            int photoAreaH  = CanvasH - 2 * Padding - BrandingH - (PhotoSlots - 1) * PhotoGap;
            int slotH       = photoAreaH / PhotoSlots;

            using var slotBg = new SolidBrush(Color.FromArgb(0xEE, 0xEE, 0xEE));

            for (int i = 0; i < Math.Min(photos.Count, PhotoSlots); i++)
            {
                int slotX = xOffset + Padding;
                int slotY = Padding + i * (slotH + PhotoGap);
                var slot  = new Rectangle(slotX, slotY, usableW, slotH);

                g.FillRectangle(slotBg, slot);

                var photo = photos[i];
                if (photo.Width > 1)
                    g.DrawImage(photo, LetterboxRect(photo.Width, photo.Height, slot));
            }

            // Branding bar extends to the bottom edge of the canvas.
            int barY    = CanvasH - Padding - BrandingH;
            var barRect = new Rectangle(xOffset, barY, StripW, BrandingH + Padding);
            using var barBrush = new SolidBrush(Color.FromArgb(0x0D, 0x1B, 0x2A));
            g.FillRectangle(barBrush, barRect);

            using var font = new Font("Arial", 13f, FontStyle.Bold, GraphicsUnit.Point);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(brandingText, font, textBrush, (RectangleF)barRect, sf);
        }

        private static Rectangle LetterboxRect(int imgW, int imgH, Rectangle slot)
        {
            float scale = Math.Min((float)slot.Width / imgW, (float)slot.Height / imgH);
            int w = (int)(imgW * scale);
            int h = (int)(imgH * scale);
            return new Rectangle(
                slot.X + (slot.Width  - w) / 2,
                slot.Y + (slot.Height - h) / 2,
                w, h);
        }

        // --- Template-based composition ------------------------------------------

        public static Bitmap ComposeFromTemplate(
            string templatePath,
            IReadOnlyList<StripSlotDefinition> slots,
            IReadOnlyList<string> photoPaths)
        {
            using var template = Image.FromFile(templatePath);

            var canvas = new Bitmap(template.Width, template.Height, PixelFormat.Format32bppArgb);
            canvas.SetResolution(template.HorizontalResolution, template.VerticalResolution);

            using var g = Graphics.FromImage(canvas);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode     = SmoothingMode.HighQuality;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            // Photos go behind — drawn first so the template frame overlays them
            foreach (var slot in slots)
            {
                int photoIndex = slot.Index - 1;
                if (photoIndex < 0 || photoIndex >= photoPaths.Count) continue;

                var slotRect = new Rectangle(
                    (int)(slot.X      * template.Width),
                    (int)(slot.Y      * template.Height),
                    (int)(slot.Width  * template.Width),
                    (int)(slot.Height * template.Height));

                try
                {
                    using var photo = Image.FromFile(photoPaths[photoIndex]);
                    g.SetClip(slotRect);
                    g.DrawImage(photo, FillRect(photo.Width, photo.Height, slotRect));
                    g.ResetClip();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not load photo {Index} for template composition", slot.Index);
                }
            }

            // Template drawn on top so its frame and graphics overlay the photos
            g.DrawImage(template, 0, 0, template.Width, template.Height);

            return canvas;
        }

        private static Rectangle FillRect(int imgW, int imgH, Rectangle slot)
        {
            float scale = Math.Max((float)slot.Width / imgW, (float)slot.Height / imgH);
            int w = (int)(imgW * scale);
            int h = (int)(imgH * scale);
            return new Rectangle(
                slot.X + (slot.Width  - w) / 2,
                slot.Y + (slot.Height - h) / 2,
                w, h);
        }

        // --- Legacy fallback -----------------------------------------------------

        private static Bitmap MakePlaceholder()
        {
            var bmp = new Bitmap(2, 2);
            bmp.SetPixel(0, 0, Color.LightGray);
            return bmp;
        }
    }
}
