using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public sealed record TextElement(
    string Id,
    MicrometreRect Bounds,
    InkChannel Ink,
    string Text,
    int FontSizePoints,
    string? FontFamily)
    : DocumentElement(Id, Bounds, Ink)
{
    public override string ElementType => "text";
    public TextFrameSizingMode FrameSizing { get; init; } = TextFrameSizingMode.Fixed;
    public TextWrappingMode Wrapping { get; init; } = TextWrappingMode.NoWrap;
    public TextOverflowMode Overflow { get; init; } = TextOverflowMode.Clip;
    public TextHorizontalAlignment HorizontalAlignment { get; init; } = TextHorizontalAlignment.Left;
    public TextVerticalAlignment VerticalAlignment { get; init; } = TextVerticalAlignment.Top;
}
