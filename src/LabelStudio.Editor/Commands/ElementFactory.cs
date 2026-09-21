using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public static class ElementFactory
{
    public static DocumentElement WithBounds(DocumentElement element, MicrometreRect bounds)
    {
        return element switch
        {
            RectangleElement rect => rect with { Bounds = bounds },
            LineElement line => RebuildLine(line, bounds),
            ImageElement img => img with { Bounds = bounds },
            TextElement text => text with { Bounds = bounds },
            _ => element,
        };
    }

    public static RectangleElement CreateRectangle(string id, MicrometreRect bounds, InkChannel ink = InkChannel.Black, bool fill = true, Micrometre? strokeWidth = null) =>
        new(id, bounds, ink, fill, strokeWidth ?? Micrometre.Zero);

    public static LineElement CreateLine(string id, MicrometrePoint start, MicrometrePoint end, Micrometre thickness, InkChannel ink = InkChannel.Black) =>
        new(id, start, end, thickness, ink);

    public static TextElement CreateText(string id, MicrometreRect bounds, string text, int fontSizePoints, InkChannel ink = InkChannel.Black, string? fontFamily = null) =>
        new(id, bounds, ink, text, fontSizePoints, fontFamily);

    public static ImageElement CreateImage(string id, MicrometreRect bounds, string assetId, InkChannel ink = InkChannel.Black) =>
        new(id, bounds, ink, assetId);

    public static LineElement WithThickness(LineElement line, Micrometre thickness) =>
        CopyLineMetadata(line, new LineElement(line.Id, line.Start, line.End, thickness, line.Ink));

    public static string GenerateId() => Guid.NewGuid().ToString("D");

    private static LineElement RebuildLine(LineElement line, MicrometreRect bounds)
    {
        MicrometrePoint start = TransformPoint(line.Start, line.Bounds, bounds, line.Thickness);
        MicrometrePoint end = TransformPoint(line.End, line.Bounds, bounds, line.Thickness);

        return CopyLineMetadata(line, new LineElement(line.Id, start, end, line.Thickness, line.Ink));
    }

    private static LineElement CopyLineMetadata(LineElement source, LineElement target) =>
        target with
        {
            Name = source.Name,
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            RotationMillidegrees = source.RotationMillidegrees,
        };

    private static MicrometrePoint TransformPoint(
        MicrometrePoint point,
        MicrometreRect oldBounds,
        MicrometreRect newBounds,
        Micrometre thickness) =>
        new(
            TransformCoordinate(point.X, oldBounds.X, oldBounds.Width, newBounds.X, newBounds.Width, thickness),
            TransformCoordinate(point.Y, oldBounds.Y, oldBounds.Height, newBounds.Y, newBounds.Height, thickness));

    private static Micrometre TransformCoordinate(
        Micrometre value,
        Micrometre oldOrigin,
        Micrometre oldSize,
        Micrometre newOrigin,
        Micrometre newSize,
        Micrometre thickness)
    {
        int halfThickness = thickness.Value / 2;
        int oldSpan = oldSize.Value - thickness.Value;
        int newSpan = newSize.Value - thickness.Value;
        if (oldSpan == 0)
        {
            return new Micrometre(newOrigin.Value + halfThickness);
        }

        double scale = newSpan / (double)oldSpan;
        return ElementGeometry.RoundToMicrometre(
            newOrigin.Value + halfThickness +
            ((value.Value - oldOrigin.Value - halfThickness) * scale));
    }
}
