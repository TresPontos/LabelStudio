using LabelStudio.Document.Ink;
using LabelStudio.Document.Elements;
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
    public int TextFontSizePoints { get; init; }
    public string? TextFontFamily { get; init; }
    public TextFrameSizingMode TextFrameSizing { get; init; } = TextFrameSizingMode.Fixed;
    public TextWrappingMode TextWrapping { get; init; } = TextWrappingMode.NoWrap;
    public TextOverflowMode TextOverflow { get; init; } = TextOverflowMode.Clip;
    public TextHorizontalAlignment TextHorizontalAlignment { get; init; } = TextHorizontalAlignment.Left;
    public TextVerticalAlignment TextVerticalAlignment { get; init; } = TextVerticalAlignment.Top;
    public int RotationMillidegrees { get; init; }
}
