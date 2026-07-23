using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Photobooth.Data.Models;
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

    // --- ComposeFromTemplate ---

    private static string CreateTempPhoto(Color fill)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        using var bmp = new Bitmap(80, 80, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(fill);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static string CreateTempTemplate(int size, Color borderColor, Rectangle transparentHole)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy; // paint the fixture's alpha exactly, no blending
            g.Clear(borderColor);
            using var clearBrush = new SolidBrush(Color.FromArgb(0, 0, 0, 0));
            g.FillRectangle(clearBrush, transparentHole);
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void ComposeFromTemplate_WithTemplateFile_DrawsPhotoThroughHoleAndFrameOverBorder()
    {
        // Exercises the template- and photo-loading code path with a real, non-null
        // template file — every other test in this class passes templatePath: null, so
        // this is the only coverage of that branch (the one that switched from
        // Image.FromFile to a stream-based read to avoid the GDI+ file-lock issue
        // documented in BitmapHelper.cs).
        const int size = 100;
        var templatePath = CreateTempTemplate(size, Color.Red, new Rectangle(20, 20, 60, 60));
        var photoPath = CreateTempPhoto(Color.Blue);
        try
        {
            var slots = new List<StripSlotDefinition>
            {
                new() { Index = 1, X = 0.2, Y = 0.2, Width = 0.6, Height = 0.6 },
            };

            using var result = PhotostripComposer.ComposeFromTemplate(templatePath, slots, new[] { photoPath });

            // Center of the transparent hole: the photo shows through.
            var centerPixel = result.GetPixel(50, 50);
            Assert.Equal(Color.Blue.ToArgb(), centerPixel.ToArgb());

            // Near the border, outside the hole: the opaque template frame covers the photo.
            var borderPixel = result.GetPixel(5, 5);
            Assert.Equal(Color.Red.ToArgb(), borderPixel.ToArgb());
        }
        finally
        {
            File.Delete(templatePath);
            File.Delete(photoPath);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void ComposeFromTemplate_NoFrameImage_ProducesDefaultSizedCanvasForAnySlotCount(int slotCount)
    {
        var photoPaths = Enumerable.Range(0, slotCount).Select(_ => CreateTempPhoto(Color.Blue)).ToList();
        try
        {
            var slots = Enumerable.Range(1, slotCount).Select(i => new StripSlotDefinition
            {
                Index  = i,
                X      = 0.1,
                Y      = (i - 1) * (1.0 / slotCount),
                Width  = 0.8,
                Height = 1.0 / slotCount,
            }).ToList();

            using var result = PhotostripComposer.ComposeFromTemplate(null, slots, photoPaths);

            Assert.Equal(1240, result.Width);   // StripW (620) * 2
            Assert.Equal(1844, result.Height);  // CanvasH
        }
        finally
        {
            foreach (var p in photoPaths) File.Delete(p);
        }
    }

    [Fact]
    public void ComposeFromTemplate_BackgroundColor_FillsAreaOutsideSlots()
    {
        var slots = new List<StripSlotDefinition>
        {
            new() { Index = 1, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.3 },
        };
        var photoPath = CreateTempPhoto(Color.Blue);
        try
        {
            using var result = PhotostripComposer.ComposeFromTemplate(
                null, slots, new[] { photoPath }, backgroundColor: "#00FF00");

            var pixel = result.GetPixel(600, 1800); // bottom-right area, outside the slot box
            Assert.Equal(Color.FromArgb(255, 0, 255, 0).ToArgb(), pixel.ToArgb());
        }
        finally
        {
            File.Delete(photoPath);
        }
    }

    [Fact]
    public void ComposeFromTemplate_TextElement_DrawsVisiblePixelsInItsBox()
    {
        var slots = new List<StripSlotDefinition>
        {
            new() { Index = 1, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.3 },
        };
        var textElements = new List<TextElementDefinition>
        {
            new() { Content = "HELLO", X = 0.0, Y = 0.85, Width = 1.0, Height = 0.1, Color = "#FFFFFF", FontSize = 24 },
        };
        var photoPath = CreateTempPhoto(Color.Blue);
        try
        {
            using var result = PhotostripComposer.ComposeFromTemplate(
                null, slots, new[] { photoPath }, backgroundColor: "#000000", textElements: textElements);

            int y0 = (int)(0.85 * 1844), y1 = (int)(0.95 * 1844);
            bool foundNonBackground = false;
            for (int x = 0; x < 620 && !foundNonBackground; x += 4)
            for (int y = y0; y < y1 && !foundNonBackground; y += 4)
                if (result.GetPixel(x, y).ToArgb() != Color.Black.ToArgb())
                    foundNonBackground = true;

            Assert.True(foundNonBackground, "Expected the text element to draw visible pixels in its box");
        }
        finally
        {
            File.Delete(photoPath);
        }
    }
}
