using System.Drawing;
using System.Drawing.Drawing2D;
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
        g.CompositingMode = CompositingMode.SourceCopy;
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
    public void Detect_ExpandPixels_ClampsAtNearEdge()
    {
        // 5x5 fill near (not at) the top-left corner: x:3-7, y:3-7 on a 50x50 canvas.
        // expandPixels=10 would put minX-10=-7 if unclamped; the clamp must floor it at 0.
        // (A stub that ignores expandPixels entirely would report X = 3/50 = 0.06, not 0.0 —
        // this is what makes the assertion below actually prove the clamp fired.)
        using var bmp = MakeBitmap(50, 50, Color.White,
            (new Rectangle(3, 3, 5, 5), Color.Green));

        var expanded = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0, expandPixels: 10);

        Assert.Single(expanded);
        Assert.Equal(0.0, expanded[0].X, 3);
        Assert.Equal(0.0, expanded[0].Y, 3);
    }

    [Fact]
    public void Detect_ExpandPixels_ClampsAtFarEdge()
    {
        // 5x5 fill near (not touching) the bottom-right edge: x:35-39, y:35-39 on a 50x50 canvas.
        // expandPixels=15 keeps the near side unclamped (35-15=20 >= 0), so this test isolates
        // only the far-edge clamp: unclamped, maxX+15=54 would push Width to 0.7; the clamp
        // must cap it at W-1=49, yielding Width=0.6 instead.
        using var bmp = MakeBitmap(50, 50, Color.White,
            (new Rectangle(35, 35, 5, 5), Color.Green));

        var expanded = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0, expandPixels: 15);

        Assert.Single(expanded);
        Assert.Equal(0.4, expanded[0].X,      3);
        Assert.Equal(0.6, expanded[0].Width,  3);
        Assert.Equal(0.4, expanded[0].Y,      3);
        Assert.Equal(0.6, expanded[0].Height, 3);
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

    [Fact]
    public void Detect_And_PunchTransparency_SlotFullyCoversPunchedHole_WhenOverlapAtLeastMargin()
    {
        // Mirrors the strip-designer defaults: Photo Overlap (expandPixels) >= Edge Margin
        // (dilatePixels) so the photo slot always fully covers the punched-transparent hole,
        // with no visible background halo between the photo and the surrounding frame.
        const int W = 100, H = 100;
        const int expandPixels = 6;
        const int dilatePixels = 3;

        using var bmp = MakeBitmap(W, H, Color.White,
            (new Rectangle(30, 30, 40, 40), Color.Green));

        var slots = TemplateSegmenter.Detect(bmp, Color.Green, tolerance: 0, expandPixels: expandPixels);
        Assert.Single(slots);
        int slotMinX = (int)Math.Round(slots[0].X * W);
        int slotMinY = (int)Math.Round(slots[0].Y * H);
        int slotMaxX = slotMinX + (int)Math.Round(slots[0].Width  * W) - 1;
        int slotMaxY = slotMinY + (int)Math.Round(slots[0].Height * H) - 1;

        using var punched = TemplateSegmenter.PunchTransparency(bmp, Color.Green, tolerance: 0, dilatePixels: dilatePixels);
        int holeMinX = W, holeMinY = H, holeMaxX = -1, holeMaxY = -1;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (punched.GetPixel(x, y).A == 0)
            {
                if (x < holeMinX) holeMinX = x;
                if (x > holeMaxX) holeMaxX = x;
                if (y < holeMinY) holeMinY = y;
                if (y > holeMaxY) holeMaxY = y;
            }
        }

        Assert.True(slotMinX <= holeMinX, $"slot left {slotMinX} must be <= hole left {holeMinX}");
        Assert.True(slotMinY <= holeMinY, $"slot top {slotMinY} must be <= hole top {holeMinY}");
        Assert.True(slotMaxX >= holeMaxX, $"slot right {slotMaxX} must be >= hole right {holeMaxX}");
        Assert.True(slotMaxY >= holeMaxY, $"slot bottom {slotMaxY} must be >= hole bottom {holeMaxY}");
    }

    [Fact]
    public void PunchTransparency_IgnoresStraySubThresholdColorMatches()
    {
        // 100x100 canvas: a real 40x40 background rectangle at (10,10)-(49,49) — 1600px,
        // well above the default 0.5% threshold (100*100*0.005 = 50px) — plus a stray 2x2
        // green patch at (80,80)-(81,81) — 4px, well below threshold — simulating a small
        // color-matching detail like a letter or logo edge that happens to share the color.
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(10, 10, 40, 40), Color.Green),
            (new Rectangle(80, 80, 2, 2), Color.Green));

        using var punched = TemplateSegmenter.PunchTransparency(bmp, Color.Green, tolerance: 0);

        Assert.Equal(0,   punched.GetPixel(30, 30).A);   // inside the large qualifying rectangle -> punched
        Assert.Equal(255, punched.GetPixel(80, 80).A);   // the tiny stray patch -> left opaque
    }

    [Fact]
    public void DetectFromTransparency_EnclosedHole_ReturnsSingleSlotWithCorrectBounds()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(25, 25, 50, 50), Color.FromArgb(0, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10);
        Assert.Single(slots);
        Assert.Equal(1, slots[0].Index);
        Assert.InRange(slots[0].X,      0.24, 0.26);
        Assert.InRange(slots[0].Y,      0.24, 0.26);
        Assert.InRange(slots[0].Width,  0.49, 0.52);
        Assert.InRange(slots[0].Height, 0.49, 0.52);
        Assert.Equal(0, slots[0].Rotation);
    }

    [Fact]
    public void DetectFromTransparency_RegionTouchingEdge_IsIgnored()
    {
        // Touches the left edge (x=0) — simulates background transparency around the
        // frame artwork, not an enclosed punched hole, and must never become a slot.
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(0, 25, 50, 50), Color.FromArgb(0, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10);
        Assert.Empty(slots);
    }

    [Fact]
    public void DetectFromTransparency_TwoEnclosedHoles_ReturnsTwoSlotsSortedTopToBottom()
    {
        using var bmp = MakeBitmap(100, 300, Color.White,
            (new Rectangle(10, 10, 80, 60), Color.FromArgb(0, 0, 0, 0)),
            (new Rectangle(10, 110, 80, 60), Color.FromArgb(0, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10);
        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots[0].Index);
        Assert.Equal(2, slots[1].Index);
        Assert.True(slots[0].Y < slots[1].Y, "Index 1 must be above Index 2");
    }

    [Fact]
    public void DetectFromTransparency_TinyHole_IsFilteredByMinAreaFraction()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(50, 50, 2, 2), Color.FromArgb(0, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp,
            alphaThreshold: 10, minAreaFraction: 0.005);
        Assert.Empty(slots);
    }

    [Fact]
    public void DetectFromTransparency_AlphaJustAboveThreshold_IsExcluded()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(25, 25, 50, 50), Color.FromArgb(11, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10);
        Assert.Empty(slots);
    }

    [Fact]
    public void DetectFromTransparency_AlphaAtThreshold_IsIncluded()
    {
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(25, 25, 50, 50), Color.FromArgb(10, 0, 0, 0)));
        var slots = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10);
        Assert.Single(slots);
    }

    [Fact]
    public void DetectFromTransparency_ExpandPixels_GrowsBoundingBoxBySpecifiedAmount()
    {
        // 40x40 hole at (30,30) on a 100x100 canvas -> occupies px x:30-69, y:30-69
        using var bmp = MakeBitmap(100, 100, Color.White,
            (new Rectangle(30, 30, 40, 40), Color.FromArgb(0, 0, 0, 0)));

        var expanded = TemplateSegmenter.DetectFromTransparency(bmp, alphaThreshold: 10, expandPixels: 5);

        Assert.Single(expanded);
        Assert.InRange(expanded[0].X,      0.24, 0.26);   // (30-5)/100 = 0.25
        Assert.InRange(expanded[0].Y,      0.24, 0.26);
        Assert.InRange(expanded[0].Width,  0.49, 0.51);   // (40+10)/100 = 0.50
        Assert.InRange(expanded[0].Height, 0.49, 0.51);
    }
}
