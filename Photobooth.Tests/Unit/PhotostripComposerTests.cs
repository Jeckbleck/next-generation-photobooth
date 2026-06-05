using System.Drawing;
using Photobooth.Print;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class PhotostripComposerTests
{
    // --- LetterboxRect ---
    // Signature: LetterboxRect(int imgW, int imgH, Rectangle slot)
    // scale = min(slot.Width / imgW, slot.Height / imgH)
    // result rect is centered inside slot

    [Theory]
    // Wide image (100x100) in tall slot (50x100):
    //   scale = min(50/100, 100/100) = 0.5 → w=50, h=50
    //   x = 0 + (50-50)/2 = 0, y = 0 + (100-50)/2 = 25
    [InlineData(100, 100, 0, 0, 50, 100,  0, 25, 50, 50)]
    // Tall image (200x100) in wide slot (100x100):
    //   scale = min(100/200, 100/100) = 0.5 → w=100, h=50
    //   x = 0 + (100-100)/2 = 0, y = 0 + (100-50)/2 = 25
    [InlineData(200, 100, 0, 0, 100, 100, 0, 25, 100, 50)]
    // Square image (100x100) in square slot (100x100):
    //   scale = 1.0 → w=100, h=100, exact fit
    [InlineData(100, 100, 0, 0, 100, 100, 0,  0, 100, 100)]
    public void LetterboxRect_ReturnsCorrectRectangle(
        int imgW, int imgH,
        int slotX, int slotY, int slotW, int slotH,
        int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var slot   = new Rectangle(slotX, slotY, slotW, slotH);
        var result = PhotostripComposer.LetterboxRect(imgW, imgH, slot);

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedW, result.Width);
        Assert.Equal(expectedH, result.Height);
    }

    // --- FillRect (crop-to-fill / cover) ---
    // Signature: FillRect(int imgW, int imgH, Rectangle slot)
    // scale = max(slot.Width / imgW, slot.Height / imgH)
    // result rect is centered inside slot (may extend outside)

    [Theory]
    // Wide image (200x100) fills square slot (100x100):
    //   scale = max(100/200, 100/100) = 1.0 → w=200, h=100
    //   x = 0 + (100-200)/2 = -50, y = 0 + (100-100)/2 = 0
    [InlineData(200, 100, 0, 0, 100, 100, -50, 0,   200, 100)]
    // Tall image (100x200) fills square slot (100x100):
    //   scale = max(100/100, 100/200) = 1.0 → w=100, h=200
    //   x = 0 + (100-100)/2 = 0, y = 0 + (100-200)/2 = -50
    [InlineData(100, 200, 0, 0, 100, 100,   0, -50, 100, 200)]
    // Square image (100x100) fills square slot (100x100):
    //   scale = 1.0 → w=100, h=100, no crop
    [InlineData(100, 100, 0, 0, 100, 100,   0,  0,  100, 100)]
    public void FillRect_ReturnsCorrectRectangle(
        int imgW, int imgH,
        int slotX, int slotY, int slotW, int slotH,
        int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var slot   = new Rectangle(slotX, slotY, slotW, slotH);
        var result = PhotostripComposer.FillRect(imgW, imgH, slot);

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedW, result.Width);
        Assert.Equal(expectedH, result.Height);
    }

    // --- RotateBitmap ---

    [Fact]
    public void RotateBitmap_90Degrees_SwapsDimensions()
    {
        using var bmp    = new Bitmap(100, 50);
        using var result = PhotostripComposer.RotateBitmap(bmp, 90);
        Assert.Equal(50,  result.Width);
        Assert.Equal(100, result.Height);
    }

    [Fact]
    public void RotateBitmap_180Degrees_PreservesDimensions()
    {
        using var bmp    = new Bitmap(100, 50);
        using var result = PhotostripComposer.RotateBitmap(bmp, 180);
        Assert.Equal(100, result.Width);
        Assert.Equal(50,  result.Height);
    }

    [Fact]
    public void RotateBitmap_0Degrees_PreservesDimensions()
    {
        using var bmp    = new Bitmap(100, 50);
        using var result = PhotostripComposer.RotateBitmap(bmp, 0);
        Assert.Equal(100, result.Width);
        Assert.Equal(50,  result.Height);
    }
}
