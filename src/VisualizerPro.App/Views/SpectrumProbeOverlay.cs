using System.Windows;
using System.Windows.Media;
using VisualizerPro.Core;

namespace VisualizerPro.App.Views;

// A small WPF visual keeps pointer feedback independent of expensive GPU plots.
internal sealed class SpectrumProbeOverlay(InstrumentView owner) : FrameworkElement
{
    private static readonly Brush Blue = FrozenBrush(125, 167, 231);
    private static readonly Pen CursorPen = FrozenPen();
    private static readonly Brush BandFill = FrozenBrush(125, 167, 231, 22);
    private static readonly Typeface Font = new("Cascadia Mono");

    protected override void OnRender(DrawingContext context) => Draw(context);

    internal void Draw(DrawingContext context)
    {
        if (owner.Kind is not (InstrumentKind.Spectrum or InstrumentKind.Spectrogram)) return;
        var bounds = owner.PlotBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (owner.IsWaterfall) { owner.DrawWaterfallProbe(context, Blue, CursorPen, Font); return; }
        if (owner.Kind == InstrumentKind.Spectrogram && owner.SelectedHistoryAge is { } selected && selected <= owner.Settings.HistorySeconds)
        {
            double y = bounds.Top + selected / owner.Settings.HistorySeconds * bounds.Height;
            context.DrawLine(CursorPen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
        double frequency = owner.CursorFrequency;
        if (bounds.Width <= 0 || bounds.Height <= 0 || frequency < owner.AxisMinimum || frequency > owner.AxisMaximum) return;
        double x = owner.FrequencyX(frequency);
        context.DrawLine(CursorPen, new Point(x, bounds.Top), new Point(x, bounds.Bottom));
        double? level = owner.CursorLevelDbfs;
        OctaveBandReading? octave = owner.Kind == InstrumentKind.Spectrum && owner.SpectrumMode == SpectrumDisplayMode.ThirdOctave ? owner.CursorOctaveBand : null;
        if (octave is { } band)
        {
            double left = owner.FrequencyX(Math.Max(owner.AxisMinimum, band.LowHz)), right = owner.FrequencyX(Math.Min(owner.AxisMaximum, band.HighHz));
            context.DrawRectangle(BandFill, null, new Rect(left, bounds.Top, Math.Max(0, right - left), bounds.Height));
        }
        bool history = owner.Kind == InstrumentKind.Spectrogram;
        if (history && owner.CursorHistoryAge is { } age)
        {
            double y = bounds.Top + age / owner.Settings.HistorySeconds * bounds.Height;
            context.DrawLine(CursorPen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }
        string reading = history
            ? $"−{owner.CursorHistoryAge ?? 0:0.00}s   {frequency:0.0} Hz   {(level is { } historicDb ? $"{historicDb:0.0} dBFS" : "—") }"
            : level is { } db ? $"{frequency:0.0} Hz   {db:0.0} dBFS   {AudioMath.Note(frequency)}" : $"{frequency:0.0} Hz";
        if (octave is { } oct)
        {
            string approximate = oct.HighHz - oct.LowHz < 2.0 * owner.Frame!.SampleRate / owner.Frame.Settings.FftSize ? "≈ " : "";
            string range = bounds.Width >= 600 ? $"[{oct.LowHz:0.#}–{oct.HighHz:0.#}]   " : "";
            reading = $"{approximate}{oct.CenterHz:0.#} Hz   {range}{oct.LevelDbfs:0.0} dBFS RMS";
        }
        var text = new FormattedText(reading, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 11, Blue, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        // Inline with the plot's top edge, with no ToolTip, popup, or opaque box.
        double textX = Math.Clamp(x + 12, bounds.Left, Math.Max(bounds.Left, bounds.Right - text.Width));
        context.DrawText(text, new Point(textX, bounds.Top - 19));
        if (!history && level is { } value)
        {
            double displayed = value + owner.Settings.TiltDbPerOctave * Math.Log2((octave?.CenterHz ?? frequency) / 1000);
            double y = bounds.Bottom - Math.Clamp((displayed - owner.Settings.FloorDb) / (owner.Settings.CeilingDb - owner.Settings.FloorDb), 0, 1) * bounds.Height;
            context.DrawEllipse(Blue, null, new Point(x, y), 3, 3);
        }
    }

    private static Brush FrozenBrush(byte r, byte g, byte b, byte alpha = 255)
    { var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b)); brush.Freeze(); return brush; }
    private static Pen FrozenPen()
    { var pen = new Pen(Blue, 1); pen.Freeze(); return pen; }
}
