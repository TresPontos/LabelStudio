using System.Globalization;

namespace LabelStudio.Editor;

public enum RulerAxis
{
    Horizontal,
    Vertical,
}

public readonly record struct RulerTick(
    double Position,
    double ValueMillimetres,
    bool IsMajor,
    string? Label);

public sealed record RulerLayout(
    RulerAxis Axis,
    double Origin,
    double ContentLengthMillimetres,
    double MajorStepMillimetres,
    double MinorStepMillimetres,
    IReadOnlyList<RulerTick> Ticks);

public readonly record struct RulerCursorMarker(
    RulerAxis Axis,
    double Position,
    double ValueMillimetres);

public sealed class RulerLayoutEngine
{
    public const double MinimumMajorTickSpacing = 60.0;

    public RulerLayout CreateLayout(
        CanvasTransform transform,
        RulerAxis axis,
        double viewportStart,
        double viewportSpan)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!double.IsFinite(viewportStart))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportStart));
        }

        if (!double.IsFinite(viewportSpan) || viewportSpan < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportSpan));
        }

        CanvasDisplayBounds bounds = transform.GetDocumentDisplayBounds();
        double origin = axis == RulerAxis.Horizontal ? bounds.Left : bounds.Top;
        double displayLength = axis == RulerAxis.Horizontal ? bounds.Width : bounds.Height;
        double contentLengthMillimetres = displayLength / transform.DipsPerMm;
        double majorStep = SelectMajorStep(transform.DipsPerMm);
        double minorStep = majorStep / 5.0;
        List<RulerTick> ticks = [];

        double visibleStart = Math.Max(origin, viewportStart);
        double visibleEnd = Math.Min(origin + displayLength, viewportStart + viewportSpan);
        if (double.IsFinite(origin) && double.IsFinite(displayLength) && displayLength >= 0 && visibleStart <= visibleEnd)
        {
            double minorSpacing = minorStep * transform.DipsPerMm;
            int first = Math.Max(0, (int)Math.Ceiling((visibleStart - origin) / minorSpacing - 1e-9));
            int last = (int)Math.Floor((visibleEnd - origin) / minorSpacing + 1e-9);
            for (int index = first; index <= last; index++)
            {
                double value = index * minorStep;
                bool isMajor = index % 5 == 0;
                ticks.Add(new(
                    origin + value * transform.DipsPerMm,
                    value,
                    isMajor,
                    isMajor ? FormatLabel(value) : null));
            }
        }

        return new(axis, origin, contentLengthMillimetres, majorStep, minorStep, ticks);
    }

    public RulerCursorMarker? CreateCursorMarker(
        CanvasTransform transform,
        RulerAxis axis,
        double position)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!double.IsFinite(position))
        {
            return null;
        }

        CanvasDisplayBounds bounds = transform.GetDocumentDisplayBounds();
        double origin = axis == RulerAxis.Horizontal ? bounds.Left : bounds.Top;
        double displayLength = axis == RulerAxis.Horizontal ? bounds.Width : bounds.Height;
        if (position < origin || position > origin + displayLength)
        {
            return null;
        }

        return new(axis, position, (position - origin) / transform.DipsPerMm);
    }

    private static double SelectMajorStep(double dipsPerMillimetre)
    {
        double minimumMillimetres = MinimumMajorTickSpacing / dipsPerMillimetre;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(minimumMillimetres)));
        foreach (double multiplier in new[] { 1.0, 2.0, 5.0, 10.0 })
        {
            double candidate = multiplier * magnitude;
            if (candidate >= minimumMillimetres)
            {
                return candidate;
            }
        }

        return 10.0 * magnitude;
    }

    private static string FormatLabel(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
