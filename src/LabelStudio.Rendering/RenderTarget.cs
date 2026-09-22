using LabelStudio.Document.Units;

namespace LabelStudio.Rendering;

public sealed record RenderTarget(
    int DpiX,
    int DpiY,
    int PhysicalTargetWidthDots,
    int PhysicalTargetHeightDots,
    MicrometreRect PrintableArea,
    int HeadLeftBlankDots,
    int PrintableWidthDots,
    IReadOnlyList<InkOutputChannel> OutputChannels)
{
    public bool SupportsRedPlane => OutputChannels.Any(channel => channel.IsRed);
    public Micrometre DocumentOriginX { get; init; } = Micrometre.Zero;
    public Micrometre DocumentOriginY { get; init; } = Micrometre.Zero;
}

public sealed record InkOutputChannel(string Name, bool IsRed);
