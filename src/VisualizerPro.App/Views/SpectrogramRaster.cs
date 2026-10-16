using System.Runtime.InteropServices;
using SkiaSharp;

namespace VisualizerPro.App.Views;

// Integrate every source row covered by a destination pixel. Ordinary bilinear
// minification can completely miss a one-row line in a long history window.
internal sealed class SpectrogramRaster : IDisposable
{
    private readonly int width, rows;
    private readonly uint[] pixels;
    private int head, count;
    public int Count => count;
    private bool dirty = true;
    private SKBitmap? bitmap;
    private SKImage? image;
    private int[] output = [];

    public SpectrogramRaster(int width, int rows) { this.width = width; this.rows = rows; pixels = new uint[width * rows]; Array.Fill(pixels, 0xFF111111u); }

    public void Append(uint[] row, int advance)
    {
        for (int i = 0; i < Math.Min(advance, rows); i++)
        {
            head = (head - 1 + rows) % rows;
            Array.Copy(row, 0, pixels, head * width, width);
        }
        count = Math.Min(rows, count + advance); dirty = true;
    }

    public SKImage Image(int height)
    {
        height = Math.Clamp(height, 1, 4096);
        if (bitmap?.Height != height)
        {
            image?.Dispose(); image = null; bitmap?.Dispose();
            bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            output = new int[width * height]; dirty = true;
        }
        if (!dirty) return image!;
        double scale = rows / (double)height;
        for (int y = 0; y < height; y++)
        {
            double start = y * scale, end = (y + 1) * scale;
            int first = (int)Math.Floor(start), last = Math.Min(rows - 1, (int)Math.Ceiling(end) - 1);
            for (int x = 0; x < width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (int sy = first; sy <= last; sy++)
                {
                    double weight = (Math.Min(end, sy + 1) - Math.Max(start, sy)) / scale;
                    uint color = sy < count ? pixels[((head + sy) % rows) * width + x] : 0xFF111111u;
                    r += ((color >> 16) & 255) * weight; g += ((color >> 8) & 255) * weight; b += (color & 255) * weight;
                }
                output[y * width + x] = unchecked((int)(0xFF000000u | ((uint)Math.Round(r) << 16) | ((uint)Math.Round(g) << 8) | (uint)Math.Round(b)));
            }
        }
        Marshal.Copy(output, 0, bitmap!.GetPixels(), output.Length); bitmap.NotifyPixelsChanged();
        image?.Dispose(); image = SKImage.FromBitmap(bitmap); dirty = false;
        return image;
    }

    public void Dispose() { image?.Dispose(); bitmap?.Dispose(); }
}
