using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using Serilog;

namespace Photobooth.Helpers
{
    internal static class BitmapHelper
    {
        // Load a BitmapImage from disk via StreamSource so WPF's URI cache is bypassed.
        // This ensures a replaced file (same path, new content) is always read fresh.
        internal static BitmapImage LoadFromFile(string path, int decodeWidth = 0)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            using var stream = File.OpenRead(path);
            bmp.StreamSource = stream;
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
                bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // Loads the thumbnail sidecar if it exists; falls back to the full image with decodeWidth.
        internal static BitmapImage LoadThumbnail(string sourcePath, int fallbackDecodeWidth)
        {
            var thumbPath = ThumbPathFor(sourcePath);
            return File.Exists(thumbPath)
                ? LoadFromFile(thumbPath)
                : LoadFromFile(sourcePath, fallbackDecodeWidth);
        }

        // Generates a JPEG thumbnail sidecar alongside the source file.
        // Returns the thumb path on success, null on failure.
        // Safe to call from a background thread.
        internal static string? GenerateThumbnail(string sourcePath, int maxWidth = 200)
        {
            var thumbPath = ThumbPathFor(sourcePath);
            try
            {
                // Read bytes first so the file handle is released immediately.
                // Image.FromFile holds a GDI+ lock for its lifetime, which blocks
                // ShowCapturedPreview and PhotostripComposer from reading the same file.
                var bytes = File.ReadAllBytes(sourcePath);
                using var ms  = new MemoryStream(bytes);
                using var src = Image.FromStream(ms); // ms must outlive src — using order handles this
                int thumbH = (int)((float)src.Height / src.Width * maxWidth);

                using var thumb = new Bitmap(maxWidth, thumbH);
                thumb.SetResolution(src.HorizontalResolution, src.VerticalResolution);
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, maxWidth, thumbH);

                var codec = ImageCodecInfo.GetImageDecoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
                thumb.Save(thumbPath, codec, ep);

                return thumbPath;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to generate thumbnail for {Path}", sourcePath);
                return null;
            }
        }

        // Derives the sidecar path from the original: photo.jpg → photo_thumb.jpg
        private static string ThumbPathFor(string sourcePath) =>
            Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                Path.GetFileNameWithoutExtension(sourcePath) + "_thumb" + Path.GetExtension(sourcePath));
    }
}
