using LabelStudio.Document.Ink;

namespace LabelStudio.Rendering;

public sealed record RenderedPlanes(
    MonochromeRaster BlackPlane,
    MonochromeRaster? RedPlane)
{
    public static RenderedPlanes BlackOnly(MonochromeRaster black) => new(black, null);
}