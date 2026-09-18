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
}