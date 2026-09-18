using LabelStudio.Document;
using LabelStudio.Document.Elements;
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

    public static string GenerateId() => Guid.NewGuid().ToString("D");

    private static LineElement RebuildLine(LineElement line, MicrometreRect bounds)
    {
        bool horizontal = line.Bounds.Width >= line.Bounds.Height;
        Micrometre half = new(line.Thickness.Value / 2);

        if (horizontal)
        {
            Micrometre y = new(bounds.Y.Value + half.Value);
            MicrometrePoint start = new(new(bounds.X.Value + half.Value), y);
            MicrometrePoint end = new(new(bounds.Right.Value - half.Value), y);
            return line with { Bounds = bounds };
        }
        else
        {
            Micrometre x = new(bounds.X.Value + half.Value);
            MicrometrePoint start = new(x, new(bounds.Y.Value + half.Value));
            MicrometrePoint end = new(x, new(bounds.Bottom.Value - half.Value));
            return line with { Bounds = bounds };
        }
    }
}