using System.Runtime.InteropServices;
using SkiaSharp;

namespace VisualizerPro.App.Views;

// Store energy, not RGB. Resample once onto physical output pixels, then apply
// the reference palette. This avoids mixing purple/yellow into muddy colors.
internal sealed class IntensitySpectrogramRaster(int width, int rows) : IDisposable
{
    private readonly float[] powers = new float[width * rows];
    private int head, count;
    private long revision;
    private (long Revision, int Width, int Height, double Left, double Right) key;
    private SKBitmap? bitmap;
    private SKImage? image;
    private float[] integrated = [];
    private int[] output = [];
    internal int Count => count;

    internal void Append(float[] levels, int advance)
    {
        for (int i = 0; i < Math.Min(advance, rows); i++)
        {
            head = (head - 1 + rows) % rows;
            for (int x = 0; x < width; x++) powers[head * width + x] = levels[x] <= -140 ? 0 : MathF.Pow(10, levels[x] * .1f);
        }
        count = Math.Min(rows, count + advance); revision++;
    }

    internal SKImage Image(int pixelWidth, int pixelHeight, double left, double right)
    {
        pixelWidth = Math.Clamp(pixelWidth, 1, 4096); pixelHeight = Math.Clamp(pixelHeight, 1, 4096);
        var next = (revision, pixelWidth, pixelHeight, left, right);
        if (image != null && key == next) return image;
        if (bitmap?.Width != pixelWidth || bitmap.Height != pixelHeight)
        {
            image?.Dispose(); image = null; bitmap?.Dispose();
            bitmap = new(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
            output = new int[pixelWidth * pixelHeight]; integrated = new float[width * pixelHeight];
        }
        double yScale = rows / (double)pixelHeight;
        // Integrate all history rows covered by a device pixel. Short events
        // retain energy through downsampling instead of blinking out.
        for (int y = 0; y < pixelHeight; y++)
        {
            double start = y * yScale, end = (y + 1) * yScale;
            int first = (int)start, last = Math.Min(rows - 1, (int)Math.Ceiling(end) - 1);
            int target = y * width; Array.Clear(integrated, target, width);
            for (int sy = first; sy <= last && sy < count; sy++)
            {
                float weight = (float)((Math.Min(end, sy + 1) - Math.Max(start, sy)) / yScale);
                int source = (head + sy) % rows * width;
                for (int x = 0; x < width; x++) integrated[target + x] += powers[source + x] * weight;
            }
        }
        double xScale = (right - left) / pixelWidth;
        for (int y = 0; y < pixelHeight; y++)
        {
            int source = y * width;
            for (int x = 0; x < pixelWidth; x++)
            {
                double at = left + (x + .5) * xScale;
                int lo = Math.Clamp((int)at, 0, width - 1), hi = Math.Min(lo + 1, width - 1);
                float energy;
                if (xScale <= 1)
                    energy = integrated[source + lo] + (integrated[source + hi] - integrated[source + lo]) * (float)Math.Clamp(at - lo, 0, 1);
                else
                {
                    // Preserve a narrow ridge when multiple source frequencies
                    // fall inside a physical display pixel.
                    int from = Math.Clamp((int)Math.Ceiling(left + x * xScale), 0, width - 1);
                    int to = Math.Clamp((int)Math.Floor(left + (x + 1) * xScale), from, width - 1);
                    energy = 0;
                    for (int sx = from; sx <= to; sx++) energy = Math.Max(energy, integrated[source + sx]);
                }
                float db = energy > 1e-14f ? 10 * MathF.Log10(energy) : -140;
                output[y * pixelWidth + x] = unchecked((int)(uint)FrequencyPalette.Intensity(db));
            }
        }
        Marshal.Copy(output, 0, bitmap!.GetPixels(), output.Length); bitmap.NotifyPixelsChanged();
        image?.Dispose(); image = SKImage.FromBitmap(bitmap); key = next;
        return image;
    }

    public void Dispose() { image?.Dispose(); bitmap?.Dispose(); }
}
