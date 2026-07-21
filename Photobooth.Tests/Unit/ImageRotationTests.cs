using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Photobooth.Helpers;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class ImageRotationTests : IDisposable
{
    private readonly string _path;

    public ImageRotationTests()
    {
        _path = Path.GetTempFileName();
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    // 40x20 JPEG, left half red (x:0-19), right half blue (x:20-39).
    private void WriteVerticalSplitJpeg()
    {
        using var bmp = new Bitmap(40, 20);
        using (var g = Graphics.FromImage(bmp))
        {
            g.FillRectangle(Brushes.Red,  0,  0, 20, 20);
            g.FillRectangle(Brushes.Blue, 20, 0, 20, 20);
        }
        bmp.Save(_path, ImageFormat.Jpeg);
    }

    private static bool IsReddish(Color c) => c.R > 150 && c.B < 100;
    private static bool IsBluish(Color c)  => c.B > 150 && c.R < 100;

    [Fact]
    public void RotateFileInPlace_ZeroDegrees_LeavesFileUnchanged()
    {
        WriteVerticalSplitJpeg();
        var before = File.ReadAllBytes(_path);

        ImageRotation.RotateFileInPlace(_path, 0);

        var after = File.ReadAllBytes(_path);
        Assert.Equal(before, after);
    }

    [Fact]
    public void RotateFileInPlace_90Degrees_SwapsDimensionsAndRotatesContentClockwise()
    {
        WriteVerticalSplitJpeg();

        ImageRotation.RotateFileInPlace(_path, 90);

        using var result = new Bitmap(_path);
        Assert.Equal(20, result.Width);
        Assert.Equal(40, result.Height);

        // Left half (red) rotates to the top half; right half (blue) to the bottom half.
        Assert.True(IsReddish(result.GetPixel(10, 5)));
        Assert.True(IsBluish(result.GetPixel(10, 35)));
    }

    [Fact]
    public void RotateFileInPlace_180Degrees_PreservesDimensionsAndRotatesContent()
    {
        WriteVerticalSplitJpeg();

        ImageRotation.RotateFileInPlace(_path, 180);

        using var result = new Bitmap(_path);
        Assert.Equal(40, result.Width);
        Assert.Equal(20, result.Height);

        // Left half (red) rotates to the right half; right half (blue) to the left half.
        Assert.True(IsBluish(result.GetPixel(10, 10)));
        Assert.True(IsReddish(result.GetPixel(30, 10)));
    }

    [Fact]
    public void RotateFileInPlace_270Degrees_SwapsDimensionsAndRotatesContentCounterclockwise()
    {
        WriteVerticalSplitJpeg();

        ImageRotation.RotateFileInPlace(_path, 270);

        using var result = new Bitmap(_path);
        Assert.Equal(20, result.Width);
        Assert.Equal(40, result.Height);

        // Left half (red) rotates to the bottom half; right half (blue) to the top half.
        Assert.True(IsBluish(result.GetPixel(10, 5)));
        Assert.True(IsReddish(result.GetPixel(10, 35)));
    }

    [Fact]
    public void RotateFileInPlace_NonexistentFile_DoesNotThrow()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var ex = Record.Exception(() => ImageRotation.RotateFileInPlace(missingPath, 90));

        Assert.Null(ex);
    }
}
