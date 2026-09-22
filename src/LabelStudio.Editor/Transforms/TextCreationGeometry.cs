using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Transforms;

public sealed record TextCreationFrame(
    MicrometreRect Bounds,
    int RotationMillidegrees,
    int FontSizePoints);

public static class TextCreationGeometry
{
    public static TextCreationFrame FromViewAlignedDrag(
        MicrometreRect documentDragBounds,
        int viewRotationDegrees)
    {
        int normalizedViewRotation = ((viewRotationDegrees % 360) + 360) % 360;
        bool swapDimensions = normalizedViewRotation is 90 or 270;
        int width = swapDimensions
            ? documentDragBounds.Height.Value
            : documentDragBounds.Width.Value;
        int height = swapDimensions
            ? documentDragBounds.Width.Value
            : documentDragBounds.Height.Value;
        double centerX = documentDragBounds.X.Value + documentDragBounds.Width.Value / 2.0;
        double centerY = documentDragBounds.Y.Value + documentDragBounds.Height.Value / 2.0;
        MicrometreRect bounds = new(
            new((int)Math.Round(centerX - width / 2.0, MidpointRounding.AwayFromZero)),
            new((int)Math.Round(centerY - height / 2.0, MidpointRounding.AwayFromZero)),
            new(width),
            new(height));

        int rotation = ElementGeometry.NormalizeAngle(-normalizedViewRotation * 1000);
        double frameHeightPoints = height / 25_400.0 * 72.0;
        int fontSize = Math.Clamp(
            (int)Math.Floor(frameHeightPoints * 0.72),
            4,
            144);
        return new TextCreationFrame(bounds, rotation, fontSize);
    }
}
