using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Elements;

public abstract record DocumentElement(
    string Id,
    MicrometreRect Bounds,
    InkChannel Ink)
{
    public abstract string ElementType { get; }
}