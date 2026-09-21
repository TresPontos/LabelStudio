using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor;

public static class HitTester
{
    public static string? HitTestTopmost(
        LabelDocument document,
        MicrometrePoint point,
        Micrometre tolerance)
    {
        return HitTestAll(document, point, tolerance).FirstOrDefault();
    }

    public static string? HitTestTopmost(
        LabelDocument document,
        MicrometrePoint point,
        Micrometre tolerance,
        HashSet<string> excludeIds)
    {
        return HitTestAll(document, point, tolerance, includeLocked: false, excludeIds).FirstOrDefault();
    }

    public static string? HitTestTopmost(
        LabelDocument document,
        MicrometrePoint point,
        Micrometre tolerance,
        bool includeLocked) =>
        HitTestAll(document, point, tolerance, includeLocked).FirstOrDefault();

    public static IReadOnlyList<string> HitTestAll(
        LabelDocument document,
        MicrometrePoint point,
        Micrometre tolerance,
        bool includeLocked = false,
        IReadOnlySet<string>? excludeIds = null)
    {
        List<string> hits = [];
        for (int i = document.Elements.Count - 1; i >= 0; i--)
        {
            DocumentElement element = document.Elements[i];
            if (excludeIds?.Contains(element.Id) == true ||
                !document.IsEffectivelyVisible(element) ||
                (!includeLocked && document.IsEffectivelyLocked(element)))
            {
                continue;
            }

            if (HitTestElement(element, point, tolerance))
            {
                hits.Add(element.Id);
            }
        }
        return hits.AsReadOnly();
    }

    public static bool HitTestElement(DocumentElement element, MicrometrePoint point, Micrometre tolerance)
    {
        return element switch
        {
            LineElement line => HitTestLine(line, point, tolerance),
            _ => HitTestRect(element.Bounds, point, tolerance),
        };
    }

    private static bool HitTestRect(MicrometreRect bounds, MicrometrePoint point, Micrometre tolerance)
    {
        return point.X >= bounds.X - tolerance
            && point.X <= bounds.Right + tolerance
            && point.Y >= bounds.Y - tolerance
            && point.Y <= bounds.Bottom + tolerance;
    }

    private static bool HitTestLine(LineElement line, MicrometrePoint point, Micrometre tolerance)
    {
        Micrometre halfThickness = new(line.Thickness.Value / 2);
        Micrometre totalTolerance = new(tolerance.Value + halfThickness.Value);

        double dx = line.End.X.Value - line.Start.X.Value;
        double dy = line.End.Y.Value - line.Start.Y.Value;
        double lengthSq = dx * dx + dy * dy;

        if (lengthSq < 1)
        {
            double distSq = (point.X.Value - line.Start.X.Value) * (point.X.Value - line.Start.X.Value)
                          + (point.Y.Value - line.Start.Y.Value) * (point.Y.Value - line.Start.Y.Value);
            return distSq <= totalTolerance.Value * totalTolerance.Value;
        }

        double t = ((point.X.Value - line.Start.X.Value) * dx + (point.Y.Value - line.Start.Y.Value) * dy) / lengthSq;
        t = Math.Clamp(t, 0.0, 1.0);

        double projX = line.Start.X.Value + t * dx;
        double projY = line.Start.Y.Value + t * dy;

        double dist = Math.Sqrt(
            (point.X.Value - projX) * (point.X.Value - projX) +
            (point.Y.Value - projY) * (point.Y.Value - projY));

        return dist <= totalTolerance.Value;
    }
}
