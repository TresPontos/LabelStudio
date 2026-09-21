using LabelStudio.Document.Units;

namespace LabelStudio.Document.Geometry;

public static class ElementGeometry
{
    public const int FullRotationMillidegrees = 360_000;

    public static int NormalizeAngle(int rotationMillidegrees)
    {
        int normalized = rotationMillidegrees % FullRotationMillidegrees;
        return normalized < 0 ? normalized + FullRotationMillidegrees : normalized;
    }

    public static IReadOnlyList<GeometryPoint> GetRotatedCorners(
        MicrometreRect bounds,
        int rotationMillidegrees)
    {
        double centreX = bounds.X.Value + (bounds.Width.Value / 2.0);
        double centreY = bounds.Y.Value + (bounds.Height.Value / 2.0);
        (double cosine, double sine) = GetCosineAndSine(rotationMillidegrees);

        return
        [
            Rotate(bounds.X.Value, bounds.Y.Value, centreX, centreY, cosine, sine),
            Rotate(bounds.Right.Value, bounds.Y.Value, centreX, centreY, cosine, sine),
            Rotate(bounds.Right.Value, bounds.Bottom.Value, centreX, centreY, cosine, sine),
            Rotate(bounds.X.Value, bounds.Bottom.Value, centreX, centreY, cosine, sine),
        ];
    }

    public static GeometryRect GetVisualBounds(MicrometreRect bounds, int rotationMillidegrees)
    {
        IReadOnlyList<GeometryPoint> corners = GetRotatedCorners(bounds, rotationMillidegrees);
        double minX = corners.Min(point => point.X);
        double minY = corners.Min(point => point.Y);
        double maxX = corners.Max(point => point.X);
        double maxY = corners.Max(point => point.Y);
        return new GeometryRect(minX, minY, maxX - minX, maxY - minY);
    }

    public static GeometryPoint InverseRotatePoint(
        MicrometrePoint point,
        MicrometreRect unrotatedBounds,
        int rotationMillidegrees) =>
        InverseRotatePoint(
            new GeometryPoint(point.X.Value, point.Y.Value),
            unrotatedBounds,
            rotationMillidegrees);

    public static GeometryPoint InverseRotatePoint(
        GeometryPoint point,
        MicrometreRect unrotatedBounds,
        int rotationMillidegrees)
    {
        double centreX = unrotatedBounds.X.Value + (unrotatedBounds.Width.Value / 2.0);
        double centreY = unrotatedBounds.Y.Value + (unrotatedBounds.Height.Value / 2.0);
        (double cosine, double sine) = GetCosineAndSine(rotationMillidegrees);
        return Rotate(point.X, point.Y, centreX, centreY, cosine, -sine);
    }

    public static Micrometre RoundToMicrometre(double value) =>
        new(checked((int)Math.Round(value, MidpointRounding.AwayFromZero)));

    public static MicrometrePoint RoundPoint(GeometryPoint point) =>
        new(RoundToMicrometre(point.X), RoundToMicrometre(point.Y));

    public static MicrometreRect RoundBounds(GeometryRect bounds) =>
        new(
            RoundToMicrometre(bounds.X),
            RoundToMicrometre(bounds.Y),
            RoundToMicrometre(bounds.Width),
            RoundToMicrometre(bounds.Height));

    private static GeometryPoint Rotate(
        double x,
        double y,
        double centreX,
        double centreY,
        double cosine,
        double sine)
    {
        double offsetX = x - centreX;
        double offsetY = y - centreY;
        return new GeometryPoint(
            centreX + (offsetX * cosine) - (offsetY * sine),
            centreY + (offsetX * sine) + (offsetY * cosine));
    }

    private static (double Cosine, double Sine) GetCosineAndSine(int rotationMillidegrees)
    {
        int normalized = NormalizeAngle(rotationMillidegrees);
        return normalized switch
        {
            0 => (1, 0),
            90_000 => (0, 1),
            180_000 => (-1, 0),
            270_000 => (0, -1),
            _ => (Math.Cos(normalized * Math.PI / 180_000.0), Math.Sin(normalized * Math.PI / 180_000.0)),
        };
    }
}
