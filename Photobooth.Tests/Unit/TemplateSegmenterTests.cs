using System.Drawing;
using Photobooth.Print;
using Xunit;

namespace Photobooth.Tests.Unit;

public class TemplateSegmenterTests
{
    private static Bitmap MakeBitmap(int w, int h, Color bg,
        params (Rectangle rect, Color color)[] fills)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(bg);
        foreach (var (rect, color) in fills)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, rect);
        }
        return bmp;
    }

    [Fact]
    public void Detect_SingleRegion_ReturnsSingleSlotWithCorrectBounds()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(25, 25, 50, 50), Color.Green));
        var slots = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0);
        Assert.Single(slots);
        Assert.Equal(1, slots[0].Index);
        Assert.InRange(slots[0].X,      0.24, 0.26);
        Assert.InRange(slots[0].Y,      0.24, 0.26);
        Assert.InRange(slots[0].Width,  0.49, 0.52);
        Assert.InRange(slots[0].Height, 0.49, 0.52);
        Assert.Equal(0, slots[0].Rotation);
    }

    [Fact]
    public void Detect_TwoSeparateRegions_ReturnsTwoSlotsSortedTopToBottom()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(10, 10, 80, 20), Color.Green),
            (new Rectangle(10, 60, 80, 20), Color.Green));
        var slots = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0);
        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots[0].Index);
        Assert.Equal(2, slots[1].Index);
        Assert.True(slots[0].Y < slots[1].Y, "Index 1 must be above Index 2");
    }

    [Fact]
    public void Detect_TinyRegion_IsFilteredByMinAreaFraction()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(50, 50, 2, 2), Color.Green));
        var slots = TemplateSegmenter.Detect(bmp, Color.Green,
            tolerance: 0, minAreaFraction: 0.005);
        Assert.Empty(slots);
    }

    [Fact]
    public void Detect_PixelWithinTolerance_IsIncluded()
    {
        var target = Color.FromArgb(100, 100, 100);
        var nearby = Color.FromArgb(110, 105, 95); // Manhattan = 10+5+5 = 20
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(10, 10, 80, 80), nearby));
        var slots = TemplateSegmenter.Detect(bmp, target, tolerance: 20);
        Assert.Single(slots);
    }

    [Fact]
    public void Detect_PixelOutsideTolerance_IsExcluded()
    {
        var target = Color.FromArgb(100, 100, 100);
        var far    = Color.FromArgb(130, 110, 90); // Manhattan = 30+10+10 = 50 > 30
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(10, 10, 80, 80), far));
        var slots = TemplateSegmenter.Detect(bmp, target, tolerance: 30);
        Assert.Empty(slots);
    }

    [Fact]
    public void Detect_ThreeRegions_AssignsIndexesTopToBottom()
    {
        using var bmp = MakeBitmap(100, 300, Color.White,
            (new Rectangle(10,  10, 80, 60), Color.Green),
            (new Rectangle(10, 110, 80, 60), Color.Green),
            (new Rectangle(10, 210, 80, 60), Color.Green));
        var slots = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0);
        Assert.Equal(3, slots.Count);
        Assert.Equal(new[] { 1, 2, 3 }, slots.Select(s => s.Index));
        Assert.True(slots[0].Y < slots[1].Y && slots[1].Y < slots[2].Y);
    }

    [Fact]
    public void Detect_ExpandPixels_GrowsBoundingBoxBySpecifiedAmount()
    {
        // 40x40 fill at (30,30) on a 100x100 canvas -> occupies px x:30-69, y:30-69
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(30, 30, 40, 40), Color.Green));

        var expanded = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0, expandPixels: 5);

        Assert.Single(expanded);
        Assert.InRange(expanded[0].X,      0.24, 0.26);   // (30-5)/100 = 0.25
        Assert.InRange(expanded[0].Y,      0.24, 0.26);
        Assert.InRange(expanded[0].Width,  0.49, 0.51);   // (40+10)/100 = 0.50
        Assert.InRange(expanded[0].Height, 0.49, 0.51);
    }

    [Fact]
    public void Detect_ExpandPixels_ClampsAtImageEdges()
    {
        // 10x10 fill touching the top-left corner -> expanding by 20 must clamp at 0, not go negative
        using var bmp = MakeBitmap(50, 50, Color.White,
            (new Rectangle(0, 0, 10, 10), Color.Green));

        var expanded = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0, expandPixels: 20);

        Assert.Single(expanded);
        Assert.Equal(0.0, expanded[0].X, 3);
        Assert.Equal(0.0, expanded[0].Y, 3);
    }

    [Fact]
    public void PunchTransparency_DilatePixels_MakesNearbyFringePixelTransparent()
    {
        // Core green block occupies x:5-9, y:5-9 on a 20x20 white canvas.
        // (10,7) sits 1px past the core's right edge — simulates an anti-aliased
        // fringe pixel that tolerance=0 alone won't match, but dilation should still punch.
        using var bmp = MakeBitmap(20, 20, Color.White,
            (new Rectangle(5, 5, 5, 5), Color.Green));

        using var punched = TemplateSegmenter.PunchTransparency(bmp, Color.Green, tolerance: 0, dilatePixels: 1);

        Assert.Equal(0,   punched.GetPixel(10, 7).A);   // within dilate radius 1 -> punched
        Assert.Equal(255, punched.GetPixel(13, 7).A);   // 4px away -> outside radius, stays opaque
    }

    [Fact]
    public void PunchTransparency_NoDilate_LeavesFringePixelOpaque()
    {
        using var bmp = MakeBitmap(20, 20, Color.White,
            (new Rectangle(5, 5, 5, 5), Color.Green));

        using var punched = TemplateSegmenter.PunchTransparency(bmp, Color.Green, tolerance: 0, dilatePixels: 0);

        Assert.Equal(255, punched.GetPixel(10, 7).A);
    }
}
