using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Transforms;

public enum ResizeAnchor
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

public sealed class AnchoredResizeGesture
{
    private readonly MicrometreRect _originalBounds;
    private readonly int _signX;
    private readonly int _signY;
    private readonly GeometryPoint _axisX;
    private readonly GeometryPoint _axisY;
    private readonly GeometryPoint _fixedAnchor;
    private readonly GeometryPoint _movingHandle;
    private readonly GeometryPoint _pointerDown;

    public AnchoredResizeGesture(
        MicrometreRect originalBounds,
        int rotationMillidegrees,
        ResizeAnchor anchor,
        GeometryPoint pointerDown)
    {
        _originalBounds = originalBounds;
        (_signX, _signY) = GetSigns(anchor);
        double radians = rotationMillidegrees / 1000.0 * Math.PI / 180.0;
        _axisX = new GeometryPoint(Math.Cos(radians), Math.Sin(radians));
        _axisY = new GeometryPoint(-Math.Sin(radians), Math.Cos(radians));
        GeometryPoint center = Center(originalBounds);
        _movingHandle = Offset(center, _signX, _signY, originalBounds.Width.Value, originalBounds.Height.Value);
        _fixedAnchor = Offset(center, -_signX, -_signY, originalBounds.Width.Value, originalBounds.Height.Value);
        _pointerDown = pointerDown;
    }

    public MicrometreRect Update(
        GeometryPoint pointer,
        int minimumWidth,
        int minimumHeight,
        bool proportional)
    {
        GeometryPoint effectiveHandle = new(
            _movingHandle.X + pointer.X - _pointerDown.X,
            _movingHandle.Y + pointer.Y - _pointerDown.Y);
        GeometryPoint delta = new(
            effectiveHandle.X - _fixedAnchor.X,
            effectiveHandle.Y - _fixedAnchor.Y);

        double width = _signX == 0
            ? _originalBounds.Width.Value
            : Math.Max(minimumWidth, _signX * Dot(delta, _axisX));
        double height = _signY == 0
            ? _originalBounds.Height.Value
            : Math.Max(minimumHeight, _signY * Dot(delta, _axisY));

        if (proportional && _signX != 0 && _signY != 0 &&
            _originalBounds.Width.Value > 0 && _originalBounds.Height.Value > 0)
        {
            double scaleX = width / _originalBounds.Width.Value;
            double scaleY = height / _originalBounds.Height.Value;
            double scale = Math.Max(scaleX, scaleY);
            width = Math.Max(minimumWidth, _originalBounds.Width.Value * scale);
            height = Math.Max(minimumHeight, _originalBounds.Height.Value * scale);
        }

        GeometryPoint center = new(
            _fixedAnchor.X + _signX * width / 2 * _axisX.X + _signY * height / 2 * _axisY.X,
            _fixedAnchor.Y + _signX * width / 2 * _axisX.Y + _signY * height / 2 * _axisY.Y);

        int finalWidth = Math.Max(minimumWidth, (int)Math.Round(width, MidpointRounding.AwayFromZero));
        int finalHeight = Math.Max(minimumHeight, (int)Math.Round(height, MidpointRounding.AwayFromZero));
        int x = (int)Math.Round(center.X - finalWidth / 2.0, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(center.Y - finalHeight / 2.0, MidpointRounding.AwayFromZero);
        return new MicrometreRect(new(x), new(y), new(finalWidth), new(finalHeight));
    }

    public static GeometryPoint GetHandlePoint(
        MicrometreRect bounds,
        int rotationMillidegrees,
        ResizeAnchor anchor)
    {
        (int signX, int signY) = GetSigns(anchor);
        double radians = rotationMillidegrees / 1000.0 * Math.PI / 180.0;
        GeometryPoint axisX = new(Math.Cos(radians), Math.Sin(radians));
        GeometryPoint axisY = new(-Math.Sin(radians), Math.Cos(radians));
        GeometryPoint center = Center(bounds);
        return new GeometryPoint(
            center.X + signX * bounds.Width.Value / 2.0 * axisX.X + signY * bounds.Height.Value / 2.0 * axisY.X,
            center.Y + signX * bounds.Width.Value / 2.0 * axisX.Y + signY * bounds.Height.Value / 2.0 * axisY.Y);
    }

    private GeometryPoint Offset(GeometryPoint center, int signX, int signY, double width, double height) =>
        new(
            center.X + signX * width / 2 * _axisX.X + signY * height / 2 * _axisY.X,
            center.Y + signX * width / 2 * _axisX.Y + signY * height / 2 * _axisY.Y);

    private static GeometryPoint Center(MicrometreRect bounds) =>
        new(bounds.X.Value + bounds.Width.Value / 2.0, bounds.Y.Value + bounds.Height.Value / 2.0);

    private static double Dot(GeometryPoint left, GeometryPoint right) =>
        left.X * right.X + left.Y * right.Y;

    private static (int X, int Y) GetSigns(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => (-1, -1),
        ResizeAnchor.Top => (0, -1),
        ResizeAnchor.TopRight => (1, -1),
        ResizeAnchor.Right => (1, 0),
        ResizeAnchor.BottomRight => (1, 1),
        ResizeAnchor.Bottom => (0, 1),
        ResizeAnchor.BottomLeft => (-1, 1),
        ResizeAnchor.Left => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
    };
}
