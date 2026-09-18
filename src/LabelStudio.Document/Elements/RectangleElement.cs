using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public sealed record RectangleElement(
    string Id,
    MicrometreRect Bounds,
    InkChannel Ink,
    bool Fill,
    Micrometre StrokeWidth)
    : DocumentElement(Id, Bounds, Ink)
{
    public override string ElementType => "rectangle";

    public static RectangleElement Create(
        string id,
        MicrometreRect bounds,
        InkChannel ink = InkChannel.Black,
        bool fill = true,
        Micrometre? strokeWidth = null)
        => new(id, bounds, ink, fill, strokeWidth ?? Micrometre.Zero);
}