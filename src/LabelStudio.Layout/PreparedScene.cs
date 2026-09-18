using LabelStudio.Document.Units;

namespace LabelStudio.Layout;

public sealed record PreparedScene(
    PhysicalSize LabelSize,
    MicrometreRect PrintableArea,
    IReadOnlyList<PreparedElement> Elements)
{
    public static PreparedScene Empty(PhysicalSize labelSize, MicrometreRect printableArea) =>
        new(labelSize, printableArea, Array.Empty<PreparedElement>());
}