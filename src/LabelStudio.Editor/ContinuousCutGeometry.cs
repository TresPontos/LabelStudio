using LabelStudio.Document.Units;

namespace LabelStudio.Editor;

public readonly record struct CanvasPoint(double X, double Y);

public readonly record struct CanvasLineSegment(CanvasPoint Start, CanvasPoint End);

public static class ContinuousCutGeometry
{
    public static CanvasLineSegment GetSegment(
        CanvasTransform transform,
        Micrometre pageWidth,
        Micrometre cutLength)
    {
        ArgumentNullException.ThrowIfNull(transform);
        (double startX, double startY) = transform.DocumentToCanvas(Micrometre.Zero, cutLength);
        (double endX, double endY) = transform.DocumentToCanvas(pageWidth, cutLength);
        return new(new(startX, startY), new(endX, endY));
    }

    public static Micrometre GetLengthFromCanvasPoint(
        CanvasTransform transform,
        double canvasX,
        double canvasY) =>
        transform.CanvasToDocument(canvasX, canvasY).Y;
}
