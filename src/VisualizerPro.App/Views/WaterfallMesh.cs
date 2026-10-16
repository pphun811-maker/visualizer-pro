using SkiaSharp;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

internal readonly record struct WaterfallHit(double Frequency, double Age, double LevelDbfs, SKPoint Position);

// An orthographic height field, shared by OpenGL, CPU capture and pointer picking.
// Native vertices are cached; managed buffers are reused across history updates.
internal sealed class WaterfallMesh : IDisposable
{
    internal readonly record struct Vertex(SKPoint Position, double U, double Age, double LevelDbfs, bool Valid);
    public readonly SKRect Bounds;
    public readonly double Minimum, Maximum, StartAge, Span, Floor, Ceiling, Tilt;
    public readonly int Columns, Rows;
    private readonly float originX, originY, frequencyWidth, skew, depthHeight, levelHeight;
    public Vertex[] Data { get; }
    private readonly ushort[] indices;
    private readonly SKPoint[] positions;
    private readonly SKColor[] colors;
    private readonly int indexCount;
    private readonly SKVertices? mesh;
    public int TriangleCount => indexCount / 3;

    public WaterfallMesh(IReadOnlyList<AnalysisFrame> history, SKRect bounds, double minimum, double maximum, double startAge, double span, AnalysisSettings settings, DisplaySettings display, WaterfallMesh? previous = null)
    {
        Bounds = bounds; Minimum = minimum; Maximum = maximum; StartAge = startAge; Span = span;
        Floor = settings.CeilingDb - settings.SpectrogramRange; Ceiling = settings.CeilingDb; Tilt = settings.TiltDbPerOctave;
        Columns = Math.Clamp((int)(bounds.Width / 3.5), 96, 192); Rows = (int)Math.Round(span * 20) + 1;
        skew = (float)(Math.Sin(display.WaterfallAzimuth * Math.PI / 180) * .33 * bounds.Width);
        depthHeight = bounds.Height * (float)(.18 + display.WaterfallElevation / 90 * .22);
        originX = bounds.Left + Math.Max(0, -skew); originY = bounds.Bottom - 12;
        frequencyWidth = Math.Max(1, bounds.Width - Math.Abs(skew)); levelHeight = Math.Max(1, bounds.Height - depthHeight - 16);
        bool reuse = previous != null && previous.Columns == Columns && previous.Rows == Rows;
        Data = reuse ? previous!.Data : new Vertex[Columns * Rows];
        positions = reuse ? previous!.positions : new SKPoint[Data.Length]; colors = reuse ? previous!.colors : new SKColor[Data.Length];
        indices = reuse ? previous!.indices : new ushort[(Rows - 1) * (Columns - 1) * 6];
        for (int r = 0; r < Rows; r++)
        {
            double age = startAge + r * span / (Rows - 1);
            var frame = FrameHistory.AtAge(history, age);
            for (int col = 0; col < Columns; col++)
            {
                double u = col / (double)(Columns - 1);
                var sample = frame == null ? (u, -140.0) : Sample(frame, col);
                double frequency = Minimum * Math.Pow(Maximum / Minimum, sample.Item1);
                var point = Project(sample.Item1, age, sample.Item2 + Tilt * Math.Log2(frequency / 1000));
                int at = r * Columns + col;
                Data[at] = new(point, sample.Item1, age, sample.Item2, frame != null); positions[at] = point;
                colors[at] = InstrumentView.Heat((sample.Item2 + Tilt * Math.Log2(frequency / 1000) - Floor) / (Ceiling - Floor));
            }
        }
        int written = 0;
        for (int r = Rows - 2; r >= 0; r--)
            for (int col = 0; col < Columns - 1; col++)
            {
                int a = r * Columns + col, b = a + 1, d = a + Columns, c = d + 1;
                if (!Data[a].Valid || !Data[b].Valid || !Data[c].Valid || !Data[d].Valid) continue;
                indices[written++] = (ushort)d; indices[written++] = (ushort)c; indices[written++] = (ushort)a;
                indices[written++] = (ushort)a; indices[written++] = (ushort)c; indices[written++] = (ushort)b;
            }
        indexCount = written;
        Array.Clear(indices, written, indices.Length - written); // Degenerate padding triangles draw no pixels.
        if (written > 0) mesh = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, null, colors, indices);
    }

    private (double, double) Sample(AnalysisFrame frame, int column)
    {
        double u = column / (double)(Columns - 1), half = .5 / (Columns - 1);
        double low = Minimum * Math.Pow(Maximum / Minimum, Math.Max(0, u - half)), high = Minimum * Math.Pow(Maximum / Minimum, Math.Min(1, u + half));
        int first = Array.BinarySearch(frame.Frequencies, low); if (first < 0) first = ~first;
        int last = Array.BinarySearch(frame.Frequencies, high); if (last < 0) last = ~last - 1;
        first = Math.Clamp(first, 0, frame.Spectrum.Length - 1); last = Math.Clamp(last, first, frame.Spectrum.Length - 1);
        int peak = first;
        for (int i = first + 1; i <= last; i++) if (frame.Spectrum[i] > frame.Spectrum[peak]) peak = i;
        double actual = column == 0 ? 0 : column == Columns - 1 ? 1 : Math.Clamp(Math.Log(frame.Frequencies[peak] / Minimum) / Math.Log(Maximum / Minimum), 0, 1);
        return (actual, frame.Spectrum[peak]);
    }

    public SKPoint Project(double u, double age, double displayedDb)
    {
        double v = (age - StartAge) / Span, z = Math.Clamp((displayedDb - Floor) / (Ceiling - Floor), 0, 1);
        return new(originX + (float)u * frequencyWidth + (float)v * skew, originY - (float)v * depthHeight - (float)z * levelHeight);
    }

    public SKPoint[] Slice(AnalysisFrame frame, double age)
    {
        var result = new SKPoint[Columns];
        for (int c = 0; c < Columns; c++)
        {
            var (u, level) = Sample(frame, c);
            double frequency = Minimum * Math.Pow(Maximum / Minimum, u);
            result[c] = Project(u, age, level + Tilt * Math.Log2(frequency / 1000));
        }
        return result;
    }

    public void Draw(SKCanvas canvas)
    {
        if (mesh == null) return;
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawVertices(mesh, SKBlendMode.Modulate, paint);
        using var stroke = new SKPaint { Color = new SKColor(125, 167, 231, 55), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = .65f };
        for (int row = Rows - 1; row >= 0; row -= Math.Max(1, Rows / 32))
        {
            using var path = new SKPath(); bool first = true;
            for (int col = 0; col < Columns; col++)
            {
                var vertex = Data[row * Columns + col];
                if (!vertex.Valid || vertex.LevelDbfs < Floor + 8) { first = true; continue; }
                if (first) { path.MoveTo(vertex.Position); first = false; } else path.LineTo(vertex.Position);
            }
            canvas.DrawPath(path, stroke);
        }
    }

    public WaterfallHit? Pick(SKPoint point)
    {
        // Reverse painter order chooses the visible frontmost triangle.
        for (int i = indexCount - 3; i >= 0; i -= 3)
        {
            var a = Data[indices[i]]; var b = Data[indices[i + 1]]; var c = Data[indices[i + 2]];
            double determinant = (b.Position.Y - c.Position.Y) * (a.Position.X - c.Position.X) + (c.Position.X - b.Position.X) * (a.Position.Y - c.Position.Y);
            if (Math.Abs(determinant) < 1e-7) continue;
            double wa = ((b.Position.Y - c.Position.Y) * (point.X - c.Position.X) + (c.Position.X - b.Position.X) * (point.Y - c.Position.Y)) / determinant;
            double wb = ((c.Position.Y - a.Position.Y) * (point.X - c.Position.X) + (a.Position.X - c.Position.X) * (point.Y - c.Position.Y)) / determinant;
            double wc = 1 - wa - wb;
            if (wa < -1e-5 || wb < -1e-5 || wc < -1e-5) continue;
            double u = wa * a.U + wb * b.U + wc * c.U;
            return new(Minimum * Math.Pow(Maximum / Minimum, u), wa * a.Age + wb * b.Age + wc * c.Age, wa * a.LevelDbfs + wb * b.LevelDbfs + wc * c.LevelDbfs, point);
        }
        return null;
    }

    public void Dispose() => mesh?.Dispose();
}
