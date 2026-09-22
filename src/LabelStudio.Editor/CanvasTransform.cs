using LabelStudio.Document.Units;

namespace LabelStudio.Editor;

public readonly record struct CanvasDisplayBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public sealed class CanvasTransform
{
    public const double BaseDipsPerMm = 3.0;
    public const double MinZoom = 0.10;
    public const double MaxZoom = 16.0;

    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;
    private int _viewRotationDegrees;
    private double _contentWidthMicrometres;
    private double _contentHeightMicrometres;

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

    public double RotationOffsetX => GetRotationOffset().X;
    public double RotationOffsetY => GetRotationOffset().Y;

    public void SetContentSize(Micrometre width, Micrometre height)
    {
        _contentWidthMicrometres = Math.Max(0, width.Value);
        _contentHeightMicrometres = Math.Max(0, height.Value);
    }

    public void SetContentSizePreservingDocumentOrigin(Micrometre width, Micrometre height)
    {
        (double beforeX, double beforeY) = DocumentToCanvas(Micrometre.Zero, Micrometre.Zero);
        SetContentSize(width, height);
        (double afterX, double afterY) = DocumentToCanvas(Micrometre.Zero, Micrometre.Zero);
        _offsetX += beforeX - afterX;
        _offsetY += beforeY - afterY;
    }

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
        (double rotationOffsetX, double rotationOffsetY) = GetRotationOffset();

        return (rx + rotationOffsetX + _offsetX, ry + rotationOffsetY + _offsetY);
    }

    public MicrometrePoint CanvasToDocument(double x, double y)
    {
        (double rotationOffsetX, double rotationOffsetY) = GetRotationOffset();
        double sx = x - _offsetX - rotationOffsetX;
        double sy = y - _offsetY - rotationOffsetY;

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

    public CanvasDisplayBounds GetDocumentDisplayBounds()
    {
        (double width, double height) = GetRotatedDisplaySize(
            _contentWidthMicrometres,
            _contentHeightMicrometres);
        return new(_offsetX, _offsetY, width, height);
    }

    public void FitToViewport(
        Micrometre contentWidth,
        Micrometre contentHeight,
        double viewportWidth,
        double viewportHeight,
        double margin)
    {
        SetContentSize(contentWidth, contentHeight);

        double baseWidth = contentWidth.Value / 1000.0 * BaseDipsPerMm;
        double baseHeight = contentHeight.Value / 1000.0 * BaseDipsPerMm;
        if (IsRotated)
        {
            (baseWidth, baseHeight) = (baseHeight, baseWidth);
        }

        double availableWidth = Math.Max(1, viewportWidth - margin * 2);
        double availableHeight = Math.Max(1, viewportHeight - margin * 2);
        Zoom = Math.Min(availableWidth / baseWidth, availableHeight / baseHeight);

        (double displayWidth, double displayHeight) = GetRotatedDisplaySize(
            contentWidth.Value,
            contentHeight.Value);
        OffsetX = (viewportWidth - displayWidth) / 2;
        OffsetY = (viewportHeight - displayHeight) / 2;
    }

    public void FitToViewportTopLeft(
        Micrometre contentWidth,
        Micrometre contentHeight,
        double viewportWidth,
        double viewportHeight,
        double padding)
    {
        SetContentSize(contentWidth, contentHeight);

        double baseWidth = contentWidth.Value / 1000.0 * BaseDipsPerMm;
        double baseHeight = contentHeight.Value / 1000.0 * BaseDipsPerMm;
        if (IsRotated)
        {
            (baseWidth, baseHeight) = (baseHeight, baseWidth);
        }

        double availableWidth = Math.Max(1, viewportWidth - padding * 2);
        double availableHeight = Math.Max(1, viewportHeight - padding * 2);
        Zoom = Math.Min(availableWidth / baseWidth, availableHeight / baseHeight);
        OffsetX = padding;
        OffsetY = padding;
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

    private (double X, double Y) GetRotationOffset()
    {
        double width = _contentWidthMicrometres / 1000.0 * DipsPerMm;
        double height = _contentHeightMicrometres / 1000.0 * DipsPerMm;

        return _viewRotationDegrees switch
        {
            90 => (height, 0),
            180 => (width, height),
            270 => (0, width),
            _ => (0, 0),
        };
    }
}
