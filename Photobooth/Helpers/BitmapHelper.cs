using System.IO;
using System.Windows.Media.Imaging;

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
    }
}
