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
    private int _viewRotationDegrees;

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

    public int ViewRotationDegrees
    {
        get => _viewRotationDegrees;
        set
        {
            int normalized = value % 360;
            if (normalized < 0) normalized += 360;
            _viewRotationDegrees = normalized switch { 0 => 0, 90 => 90, 180 => 180, 270 => 270, _ => _viewRotationDegrees };
        }
    }

    public double DipsPerMm => BaseDipsPerMm * _zoom;

    public bool IsRotated => _viewRotationDegrees is 90 or 270;

    public (double Cos, double Sin) RotationMatrix =>
        _viewRotationDegrees switch
        {
            0 => (1.0, 0.0),
            90 => (0.0, 1.0),
            180 => (-1.0, 0.0),
            270 => (0.0, -1.0),
            _ => (1.0, 0.0),
        };

    public double DocumentToCanvasX(Micrometre x) => DocumentToCanvas(x, Micrometre.Zero).X;
    public double DocumentToCanvasY(Micrometre y) => DocumentToCanvas(Micrometre.Zero, y).Y;

    public double DocumentToCanvasLength(Micrometre length) =>
        length.Value / 1000.0 * DipsPerMm;

    public (double X, double Y) DocumentToCanvas(MicrometrePoint point) =>
        DocumentToCanvas(point.X, point.Y);

    public (double X, double Y) DocumentToCanvas(Micrometre x, Micrometre y)
    {
        double dx = x.Value / 1000.0 * DipsPerMm;
        double dy = y.Value / 1000.0 * DipsPerMm;

        (double cos, double sin) = RotationMatrix;
        double rx = dx * cos - dy * sin;
        double ry = dx * sin + dy * cos;

        return (rx + _offsetX, ry + _offsetY);
    }

    public MicrometrePoint CanvasToDocument(double x, double y)
    {
        double sx = x - _offsetX;
        double sy = y - _offsetY;

        (double cos, double sin) = RotationMatrix;
        double rx = sx * cos + sy * sin;
        double ry = -sx * sin + sy * cos;

        return new(
            new((int)Math.Round(rx / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero)),
            new((int)Math.Round(ry / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero)));
    }

    public Micrometre CanvasToDocumentLength(double length) =>
        new((int)Math.Round(length / DipsPerMm * 1000.0, MidpointRounding.AwayFromZero));

    public Micrometre ScreenToleranceToDocument(double screenToleranceDip) =>
        CanvasToDocumentLength(screenToleranceDip);

    public void ZoomAtPoint(double newZoom, double centerX, double centerY)
    {
        MicrometrePoint docPoint = CanvasToDocument(centerX, centerY);
        Zoom = newZoom;
        (double cx, double cy) = DocumentToCanvas(docPoint);
        _offsetX += centerX - cx;
        _offsetY += centerY - cy;
    }

    public (double Width, double Height) GetRotatedDisplaySize(double docWidthUm, double docHeightUm)
    {
        double w = docWidthUm / 1000.0 * DipsPerMm;
        double h = docHeightUm / 1000.0 * DipsPerMm;
        return IsRotated ? (h, w) : (w, h);
    }

    public void Reset()
    {
        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        _viewRotationDegrees = 0;
    }

    public void ResetView()
    {
        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
    }
}