// Photobooth/Camera/EvfDecoder.cs
using System;
using System.IO;
using System.Windows.Media.Imaging;
using Serilog;

namespace Photobooth.Camera;

internal static class EvfDecoder
{
    // Returns null on empty input or decode failure; never throws.
    // rotationDegrees must be one of 0, 90, 180, 270 (clockwise); any other value behaves as 0.
    internal static BitmapSource? Decode(byte[] data, int rotationDegrees = 0)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.Rotation = rotationDegrees switch
            {
                90  => Rotation.Rotate90,
                180 => Rotation.Rotate180,
                270 => Rotation.Rotate270,
                _   => Rotation.Rotate0,
            };
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
