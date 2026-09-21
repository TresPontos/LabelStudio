namespace LabelStudio.Document.Geometry;

public readonly record struct GeometryRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
