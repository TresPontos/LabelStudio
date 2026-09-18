using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public sealed record LineElement(
    string Id,
    MicrometrePoint Start,
    MicrometrePoint End,
    Micrometre Thickness,
    InkChannel Ink)
    : DocumentElement(
        Id,
        ComputeBounds(Start, End, Thickness),
        Ink)
{
    public override string ElementType => "line";

    public MicrometrePoint Start { get; init; } = Start;
    public MicrometrePoint End { get; init; } = End;
    public Micrometre Thickness { get; init; } = Thickness;

    private static MicrometreRect ComputeBounds(MicrometrePoint start, MicrometrePoint end, Micrometre thickness)
    {
        Micrometre minX = Micrometre.Min(start.X, end.X);
        Micrometre minY = Micrometre.Min(start.Y, end.Y);
        Micrometre maxX = Micrometre.Max(start.X, end.X);
        Micrometre maxY = Micrometre.Max(start.Y, end.Y);
        Micrometre half = new(thickness.Value / 2);
        return new MicrometreRect(
            minX - half,
            minY - half,
            maxX - minX + thickness,
            maxY - minY + thickness);
    }
}