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
        double minAreaFraction = 0.005)
    {
        int W = template.Width;
        int H = template.Height;
        int minPixels = Math.Max(1, (int)(W * H * minAreaFraction));

        // Lock pixels into a flat byte array for fast access.
        // Format32bppArgb in memory: [B, G, R, A] per pixel.
        var bd = template.LockBits(
            new Rectangle(0, 0, W, H),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        int stride = Math.Abs(bd.Stride);
        byte[] pixels = new byte[stride * H];
        try
        {
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            template.UnlockBits(bd);
        }

        byte tR = sampledColor.R, tG = sampledColor.G, tB = sampledColor.B;
        bool[] visited = new bool[W * H];
        var regions = new List<(int count, int minX, int minY, int maxX, int maxY)>();

        int[] dx = { -1, 1,  0, 0 };
        int[] dy = {  0, 0, -1, 1 };

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int idx = y * W + x;
            if (visited[idx]) continue;

            int off = y * stride + x * 4;
            // off+2=R, off+1=G, off+0=B (Format32bppArgb byte order)
            if (!IsMatch(pixels[off + 2], pixels[off + 1], pixels[off], tR, tG, tB, tolerance))
                continue;

            var queue = new Queue<int>();
            queue.Enqueue(idx);
            visited[idx] = true;

            int count = 0, minX = x, minY = y, maxX = x, maxY = y;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
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
                    if (visited[nIdx]) continue;
                    int nOff = ny * stride + nx * 4;
                    if (!IsMatch(pixels[nOff + 2], pixels[nOff + 1], pixels[nOff], tR, tG, tB, tolerance))
                        continue;
                    visited[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }

            if (count >= minPixels)
                regions.Add((count, minX, minY, maxX, maxY));
        }

        return regions
            .OrderBy(r => r.minY)
            .Select((r, i) => new StripSlotDefinition
            {
                Index    = i + 1,
                X        = (double)r.minX / W,
                Y        = (double)r.minY / H,
                Width    = (double)(r.maxX - r.minX + 1) / W,
                Height   = (double)(r.maxY - r.minY + 1) / H,
                Rotation = 0,
            })
            .ToList();
    }

    private static bool IsMatch(byte r, byte g, byte b, byte tR, byte tG, byte tB, int tolerance)
        => Math.Abs(r - tR) + Math.Abs(g - tG) + Math.Abs(b - tB) <= tolerance;
}
