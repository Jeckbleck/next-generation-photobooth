using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Serilog;

namespace Photobooth.Helpers
{
    internal static class ImageRotation
    {
        // Rotates a JPEG file on disk in place. No-op when degrees is 0.
        // degrees must be one of 0, 90, 180, 270 (clockwise); any other value is a no-op.
        // Never throws — I/O or decode failures are logged and the file is left untouched.
        internal static void RotateFileInPlace(string path, int degrees)
        {
            RotateFlipType flip = degrees switch
            {
                90  => RotateFlipType.Rotate90FlipNone,
                180 => RotateFlipType.Rotate180FlipNone,
                270 => RotateFlipType.Rotate270FlipNone,
                _   => RotateFlipType.RotateNoneFlipNone,
            };
            if (flip == RotateFlipType.RotateNoneFlipNone) return;

            try
            {
                // Read bytes first so the file handle is released immediately — Image.FromStream
                // would otherwise hold a GDI+ lock on the source file for the lifetime of the
                // Image, which would block the re-save back to the same path below.
                var bytes = File.ReadAllBytes(path);
                using var ms  = new MemoryStream(bytes);
                using var src = Image.FromStream(ms);
                src.RotateFlip(flip);

                var codec = ImageCodecInfo.GetImageDecoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                // Re-encode at quality 95 strips EXIF and re-compresses, but a one-time tradeoff for photobooth (not cumulative).
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                src.Save(path, codec, ep);
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "Failed to rotate photo {Path} by {Degrees}°", path, degrees);
            }
        }
    }
}
