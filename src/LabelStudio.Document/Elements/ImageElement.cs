using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public sealed record ImageElement(
    string Id,
    MicrometreRect Bounds,
    InkChannel Ink,
    string AssetId)
    : DocumentElement(Id, Bounds, Ink)
{
    public override string ElementType => "image";

    public bool LockAspectRatio { get; init; } = true;
}
