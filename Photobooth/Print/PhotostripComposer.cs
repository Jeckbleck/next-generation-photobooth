using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
            // Backing streams for images loaded below — Image.FromStream requires the
            // stream to stay open for the image's whole lifetime, so these are disposed
            // alongside (after) the images, not at the end of the loop body.
            var streams = new List<MemoryStream>(PhotoSlots);
            try
            {
                foreach (var path in photoPaths)
                {
                    try
                    {
                        // Read bytes up front instead of Image.FromFile, which keeps a
                        // GDI+ lock on the file for the image's whole lifetime — see the
                        // same fix and rationale in Helpers/BitmapHelper.cs.
                        var stream = new MemoryStream(File.ReadAllBytes(path));
                        streams.Add(stream);
                        images.Add(Image.FromStream(stream));
                    }
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
                foreach (var s in streams) s.Dispose();
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

        internal static Rectangle LetterboxRect(int imgW, int imgH, Rectangle slot)
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
            string? templatePath,
            IReadOnlyList<StripSlotDefinition> slots,
            IReadOnlyList<string> photoPaths,
            string? backgroundColor = null,
            IReadOnlyList<TextElementDefinition>? textElements = null)
        {
            // Read the file's bytes up front and load from a MemoryStream instead of
            // Image.FromFile, which keeps a GDI+ lock on the file for the whole lifetime
            // of the returned Image — see the same fix and rationale in
            // Helpers/BitmapHelper.cs. The stream must stay alive for as long as
            // `template` is used (Image.FromStream's documented contract), so it's
            // disposed in the same finally block below, not right after this line.
            MemoryStream? templateStream = null;
            Image?        template       = null;
            if (templatePath is not null && File.Exists(templatePath))
            {
                templateStream = new MemoryStream(File.ReadAllBytes(templatePath));
                template       = Image.FromStream(templateStream);
            }

            try
            {
                int singleW = template?.Width  ?? StripW;
                int singleH = template?.Height ?? CanvasH;

                // Step 1 — compose a single strip at the template's native resolution (or the default size)
                using var single = new Bitmap(singleW, singleH, PixelFormat.Format32bppArgb);
                single.SetResolution(
                    template?.HorizontalResolution ?? 300,
                    template?.VerticalResolution   ?? 300);

                using (var g = Graphics.FromImage(single))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode     = SmoothingMode.HighQuality;
                    g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

                    if (!string.IsNullOrEmpty(backgroundColor))
                    {
                        try
                        {
                            using var bgBrush = new SolidBrush(ColorTranslator.FromHtml(backgroundColor));
                            g.FillRectangle(bgBrush, 0, 0, singleW, singleH);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Invalid background color '{Color}' — skipping fill", backgroundColor);
                        }
                    }

                    // Photos behind so the template frame overlays them
                    foreach (var slot in slots)
                    {
                        int photoIndex = slot.Index - 1;
                        if (photoIndex < 0 || photoIndex >= photoPaths.Count) continue;

                        var slotRect = new Rectangle(
                            (int)(slot.X      * singleW),
                            (int)(slot.Y      * singleH),
                            (int)(slot.Width  * singleW),
                            (int)(slot.Height * singleH));

                        try
                        {
                            // Same file-lock fix as the template load above — the stream
                            // and image are declared together so `using` disposes the
                            // image first, then the stream, on this block's exit.
                            using var photoStream = new MemoryStream(File.ReadAllBytes(photoPaths[photoIndex]));
                            using var photo = Image.FromStream(photoStream);

                            Bitmap? rotated = null;
                            Image   drawPhoto = photo;
                            if (slot.Rotation != 0)
                            {
                                rotated   = RotateBitmap((Bitmap)photo, slot.Rotation);
                                drawPhoto = rotated;
                            }

                            try
                            {
                                g.SetClip(slotRect);
                                g.DrawImage(drawPhoto, FillRect(drawPhoto.Width, drawPhoto.Height, slotRect));
                                g.ResetClip();
                            }
                            finally
                            {
                                rotated?.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Could not load photo {Index} for template composition", slot.Index);
                        }
                    }

                    // Template frame on top, if provided
                    if (template is not null)
                        g.DrawImage(template, 0, 0, singleW, singleH);

                    // Text elements on top of everything
                    if (textElements is not null)
                    {
                        foreach (var text in textElements)
                        {
                            var textRect = new RectangleF(
                                (float)(text.X      * singleW),
                                (float)(text.Y      * singleH),
                                (float)(text.Width  * singleW),
                                (float)(text.Height * singleH));

                            try
                            {
                                using var font      = new Font("Arial", (float)text.FontSize, FontStyle.Bold, GraphicsUnit.Point);
                                using var textBrush = new SolidBrush(ColorTranslator.FromHtml(text.Color));
                                var sf = new StringFormat
                                {
                                    Alignment     = StringAlignment.Center,
                                    LineAlignment = StringAlignment.Center,
                                };
                                g.DrawString(text.Content, font, textBrush, textRect, sf);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Could not render text element '{Content}'", text.Content);
                            }
                        }
                    }
                }

                // Step 2 — duplicate side by side so the DNP 2-inch cut produces two identical strips
                var canvas = new Bitmap(singleW * 2, singleH, PixelFormat.Format32bppArgb);
                canvas.SetResolution(
                    template?.HorizontalResolution ?? 300,
                    template?.VerticalResolution   ?? 300);

                using var gc = Graphics.FromImage(canvas);
                gc.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gc.DrawImage(single, 0,       0);
                gc.DrawImage(single, singleW, 0);

                return canvas;
            }
            finally
            {
                template?.Dispose();
                templateStream?.Dispose();
            }
        }

        internal static Rectangle FillRect(int imgW, int imgH, Rectangle slot)
        {
            float scale = Math.Max((float)slot.Width / imgW, (float)slot.Height / imgH);
            int w = (int)(imgW * scale);
            int h = (int)(imgH * scale);
            return new Rectangle(
                slot.X + (slot.Width  - w) / 2,
                slot.Y + (slot.Height - h) / 2,
                w, h);
        }

        // Rotates a bitmap clockwise by the given degrees (must be 90, 180, or 270).
        internal static Bitmap RotateBitmap(Bitmap source, int degrees)
        {
            bool swap = degrees % 180 != 0;
            int newW = swap ? source.Height : source.Width;
            int newH = swap ? source.Width  : source.Height;

            var bmp = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            bmp.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.TranslateTransform(newW / 2f, newH / 2f);
            g.RotateTransform(degrees);
            g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
            g.DrawImage(source, 0, 0);

            return bmp;
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
