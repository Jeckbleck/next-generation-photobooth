using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Photobooth.Data.Models;

namespace Photobooth.Print;

public static class TemplateSegmenter
{
    public static List<StripSlotDefinition> Detect(
        Bitmap template,
        Color  sampledColor,
        int    tolerance       = 15,
        double minAreaFraction = 0.005,
        int    expandPixels    = 0)
    {
        int W = template.Width;
        int H = template.Height;
        int minPixels = Math.Max(1, (int)(W * H * minAreaFraction));

        byte[] pixels;
        int stride;
        var bd = template.LockBits(
            new Rectangle(0, 0, W, H),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        stride = Math.Abs(bd.Stride);
        pixels = new byte[stride * H];
        try
        {
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            template.UnlockBits(bd);
        }

        byte tR = sampledColor.R, tG = sampledColor.G, tB = sampledColor.B;
        var mask = BuildMask(pixels, stride, W, H, tR, tG, tB, tolerance);
        var (regions, _) = FindQualifyingRegions(mask, W, H, minPixels);

        return regions
            .OrderBy(r => r.minY)
            .Select((r, i) =>
            {
                int ex0 = Math.Max(0,     r.minX - expandPixels);
                int ey0 = Math.Max(0,     r.minY - expandPixels);
                int ex1 = Math.Min(W - 1, r.maxX + expandPixels);
                int ey1 = Math.Min(H - 1, r.maxY + expandPixels);
                return new StripSlotDefinition
                {
                    Index    = i + 1,
                    X        = (double)ex0 / W,
                    Y        = (double)ey0 / H,
                    Width    = (double)(ex1 - ex0 + 1) / W,
                    Height   = (double)(ey1 - ey0 + 1) / H,
                    Rotation = 0,
                };
            })
            .ToList();
    }

    // Detects photo windows from a template that already has them punched out as real
    // transparency, instead of a flat placeholder color. A region only counts as a photo
    // window if it's fully enclosed by opaque pixels — background transparency around the
    // frame artwork (common on templates designed as overlays) always touches the image's
    // outer edge, so it's never mistaken for a slot.
    public static List<StripSlotDefinition> DetectFromTransparency(
        Bitmap template,
        int    alphaThreshold  = 10,
        double minAreaFraction = 0.005,
        int    expandPixels    = 0)
    {
        int W = template.Width;
        int H = template.Height;
        int minPixels = Math.Max(1, (int)(W * H * minAreaFraction));

        byte[] pixels;
        int stride;
        var bd = template.LockBits(
            new Rectangle(0, 0, W, H),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        stride = Math.Abs(bd.Stride);
        pixels = new byte[stride * H];
        try
        {
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            template.UnlockBits(bd);
        }

        var mask = BuildAlphaMask(pixels, stride, W, H, alphaThreshold);
        var (regions, _) = FindQualifyingRegions(mask, W, H, minPixels, requireEnclosed: true);

        return regions
            .OrderBy(r => r.minY)
            .Select((r, i) =>
            {
                int ex0 = Math.Max(0,     r.minX - expandPixels);
                int ey0 = Math.Max(0,     r.minY - expandPixels);
                int ex1 = Math.Min(W - 1, r.maxX + expandPixels);
                int ey1 = Math.Min(H - 1, r.maxY + expandPixels);
                return new StripSlotDefinition
                {
                    Index    = i + 1,
                    X        = (double)ex0 / W,
                    Y        = (double)ey0 / H,
                    Width    = (double)(ex1 - ex0 + 1) / W,
                    Height   = (double)(ey1 - ey0 + 1) / H,
                    Rotation = 0,
                };
            })
            .ToList();
    }

    // Builds a per-pixel bool mask of "is this pixel transparent enough to be inside a
    // photo window" — the alpha-channel counterpart to BuildMask's color-distance check.
    private static bool[] BuildAlphaMask(byte[] pixels, int stride, int W, int H, int alphaThreshold)
    {
        var mask = new bool[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int off = y * stride + x * 4;
            mask[y * W + x] = pixels[off + 3] <= alphaThreshold;
        }
        return mask;
    }

    // Returns a copy of the template with every pixel belonging to a large-enough
    // (minAreaFraction-qualifying) connected region matching sampledColor — grown by
    // dilatePixels — made fully transparent, so the photo underneath shows through where
    // the operator marked a photo window with a flat placeholder color. Small stray color
    // matches (e.g. a letter or logo edge that happens to be a similar color) never qualify
    // as a region and are left untouched, instead of being punched transparent too.
    public static Bitmap PunchTransparency(
        Bitmap template,
        Color  sampledColor,
        int    tolerance,
        int    dilatePixels    = 0,
        double minAreaFraction = 0.005)
    {
        int W = template.Width;
        int H = template.Height;
        int minPixels = Math.Max(1, (int)(W * H * minAreaFraction));

        var result = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
            g.DrawImage(template, 0, 0, W, H);

        var bd = result.LockBits(
            new Rectangle(0, 0, W, H),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        int stride = Math.Abs(bd.Stride);
        byte[] pixels = new byte[stride * H];
        try
        {
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);

            byte tR = sampledColor.R, tG = sampledColor.G, tB = sampledColor.B;
            var colorMask = BuildMask(pixels, stride, W, H, tR, tG, tB, tolerance);
            var (_, regionMask) = FindQualifyingRegions(colorMask, W, H, minPixels);

            var punchMask = dilatePixels > 0 ? Dilate(regionMask, W, H, dilatePixels) : regionMask;

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (punchMask[y * W + x])
                    pixels[y * stride + x * 4 + 3] = 0;
            }

            Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
        }
        finally
        {
            result.UnlockBits(bd);
        }

        return result;
    }

    // Builds a per-pixel bool mask of "is this pixel within tolerance of sampledColor".
    // Shared by Detect (connected-component search) and PunchTransparency (alpha zeroing)
    // so both operate on identical color-matching logic.
    private static bool[] BuildMask(byte[] pixels, int stride, int W, int H,
        byte tR, byte tG, byte tB, int tolerance)
    {
        var mask = new bool[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int off = y * stride + x * 4;
            mask[y * W + x] = IsMatch(pixels[off + 2], pixels[off + 1], pixels[off], tR, tG, tB, tolerance);
        }
        return mask;
    }

    // Runs 4-connected BFS over a color-match mask and returns only the components whose
    // pixel count meets minPixels — both as bounding-box regions (for Detect's slot
    // placement) and as a flattened per-pixel mask covering every pixel of every qualifying
    // component (for PunchTransparency, so stray sub-threshold matches are never punched).
    private static (List<(int count, int minX, int minY, int maxX, int maxY)> regions, bool[] regionMask)
        FindQualifyingRegions(bool[] mask, int W, int H, int minPixels, bool requireEnclosed = false)
    {
        bool[] visited    = new bool[W * H];
        bool[] regionMask = new bool[W * H];
        var regions = new List<(int count, int minX, int minY, int maxX, int maxY)>();

        int[] dx = { -1, 1,  0, 0 };
        int[] dy = {  0, 0, -1, 1 };

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int idx = y * W + x;
            if (visited[idx] || !mask[idx]) continue;

            var queue = new Queue<int>();
            var componentPixels = new List<int>();
            queue.Enqueue(idx);
            visited[idx] = true;

            int count = 0, minX = x, minY = y, maxX = x, maxY = y;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                componentPixels.Add(cur);
                int cx  = cur % W;
                int cy  = cur / W;
                count++;
                if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if ((uint)nx >= (uint)W || (uint)ny >= (uint)H) continue;
                    int nIdx = ny * W + nx;
                    if (visited[nIdx] || !mask[nIdx]) continue;
                    visited[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }

            bool touchesEdge = minX == 0 || minY == 0 || maxX == W - 1 || maxY == H - 1;
            if (count >= minPixels && (!requireEnclosed || !touchesEdge))
            {
                regions.Add((count, minX, minY, maxX, maxY));
                foreach (var p in componentPixels)
                    regionMask[p] = true;
            }
        }

        return (regions, regionMask);
    }

    // Grows a bool mask outward by `radius` pixels in every direction (Chebyshev/square
    // dilation) using two separable O(W*H) passes — horizontal nearest-true-neighbor scan,
    // then the same scan applied vertically to the horizontal result — instead of an
    // O(W*H*radius^2) naive per-pixel neighborhood check.
    private static bool[] Dilate(bool[] mask, int W, int H, int radius)
    {
        var horiz = new bool[W * H];
        for (int y = 0; y < H; y++)
        {
            int lastTrue = int.MinValue / 2;
            for (int x = 0; x < W; x++)
            {
                if (mask[y * W + x]) lastTrue = x;
                if (x - lastTrue <= radius) horiz[y * W + x] = true;
            }
            lastTrue = int.MaxValue / 2;
            for (int x = W - 1; x >= 0; x--)
            {
                if (mask[y * W + x]) lastTrue = x;
                if (lastTrue - x <= radius) horiz[y * W + x] = true;
            }
        }

        var result = new bool[W * H];
        for (int x = 0; x < W; x++)
        {
            int lastTrue = int.MinValue / 2;
            for (int y = 0; y < H; y++)
            {
                if (horiz[y * W + x]) lastTrue = y;
                if (y - lastTrue <= radius) result[y * W + x] = true;
            }
            lastTrue = int.MaxValue / 2;
            for (int y = H - 1; y >= 0; y--)
            {
                if (horiz[y * W + x]) lastTrue = y;
                if (lastTrue - y <= radius) result[y * W + x] = true;
            }
        }
        return result;
    }

    private static bool IsMatch(byte r, byte g, byte b, byte tR, byte tG, byte tB, int tolerance)
        => Math.Abs(r - tR) + Math.Abs(g - tG) + Math.Abs(b - tB) <= tolerance;
}
