using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public abstract record DocumentElement(
    string Id,
    MicrometreRect Bounds,
    InkChannel Ink)
{
    public abstract string ElementType { get; }

    public string? Name { get; init; }
    public bool IsVisible { get; init; } = true;
    public bool IsLocked { get; init; }
    public int RotationMillidegrees { get; init; }
}
