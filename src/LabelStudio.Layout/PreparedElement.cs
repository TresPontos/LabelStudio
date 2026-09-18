using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Layout;

public sealed record PreparedElement(
    string SourceElementId,
    MicrometreRect Bounds,
    InkChannel Ink,
    PreparedContent Content)
{
    public bool IsFilled { get; init; }
    public Micrometre StrokeWidth { get; init; } = Micrometre.Zero;
    public Micrometre LineThickness { get; init; } = Micrometre.Zero;
}