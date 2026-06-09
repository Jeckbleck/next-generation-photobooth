// Photobooth/Camera/EvfDecoder.cs
using System;
using System.IO;
using System.Windows.Media.Imaging;
using Serilog;

namespace Photobooth.Camera;

internal static class EvfDecoder
{
    // Returns null on empty input or decode failure; never throws.
    internal static BitmapSource? Decode(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to decode EVF frame");
            return null;
        }
    }
}
