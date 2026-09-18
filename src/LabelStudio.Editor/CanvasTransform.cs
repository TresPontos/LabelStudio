using LabelStudio.Document.Units;

namespace LabelStudio.Editor;

public sealed class CanvasTransform
{
    public const double BaseDipsPerMm = 3.0;
    public const double MinZoom = 0.10;
    public const double MaxZoom = 16.0;

    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;

    public double Zoom
    {
        get => _zoom;
        set => _zoom = Math.Clamp(value, MinZoom, MaxZoom);
    }

    public double OffsetX
    {
        get => _offsetX;
        set => _offsetX = value;
    }

    public double OffsetY
    {
        get => _offsetY;
        set => _offsetY = value;
    }

    public double DipsPerMm => BaseDipsPerMm * _zoom;

    public double DocumentToCanvasX(Micrometre x) =>
        x.Value / 1000.0 * DipsPerMm + _offsetX;

    public double DocumentToCanvasY(Micrometre y) =>
        y.Value / 1000.0 * DipsPerMm + _offsetY;

    public double DocumentToCanvasLength(Micrometre length) =>
        length.Value / 1000.0 * DipsPerMm;

    public (double X, double Y) DocumentToCanvas(MicrometrePoint point) =>
        (DocumentToCanvasX(point.X), DocumentToCanvasY(point.Y));

    public MicrometrePoint CanvasToDocument(double x, double y) =>
        new(
            new((int)Math.Round((x - _offsetX) / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero)),
            new((int)Math.Round((y - _offsetY) / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero)));

    public Micrometre CanvasToDocumentLength(double length) =>
        new((int)Math.Round(length / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero));

    public Micrometre ScreenToleranceToDocument(double screenToleranceDip) =>
        CanvasToDocumentLength(screenToleranceDip);

    public void ZoomAtPoint(double newZoom, double centerX, double centerY)
    {
        MicrometrePoint docPoint = CanvasToDocument(centerX, centerY);
        Zoom = newZoom;
        _offsetX = centerX - docPoint.X.Value / 1000.0 * DipsPerMm;
        _offsetY = centerY - docPoint.Y.Value / 1000.0 * DipsPerMm;
    }

    public void Reset()
    {
        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
    }
}