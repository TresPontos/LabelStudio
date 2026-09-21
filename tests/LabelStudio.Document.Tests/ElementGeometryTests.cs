using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Tests;

public class ElementGeometryTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(360000, 0)]
    [InlineData(450000, 90000)]
    [InlineData(-90000, 270000)]
    public void NormalizeAngle_ReturnsCanonicalClockwiseAngle(int angle, int expected) =>
        Assert.Equal(expected, ElementGeometry.NormalizeAngle(angle));

    [Fact]
    public void RotatedCorners_ClockwiseRightAngleAroundCentre()
    {
        MicrometreRect bounds = new(new(100), new(200), new(400), new(200));

        IReadOnlyList<GeometryPoint> corners = ElementGeometry.GetRotatedCorners(bounds, 90_000);

        AssertPoint(corners[0], 400, 100);
        AssertPoint(corners[1], 400, 500);
        AssertPoint(corners[2], 200, 500);
        AssertPoint(corners[3], 200, 100);
        Assert.Equal(new GeometryRect(200, 100, 200, 400), ElementGeometry.GetVisualBounds(bounds, 90_000));
    }

    [Fact]
    public void VisualBounds_ArbitraryAngleMatchesRotatedExtents()
    {
        MicrometreRect bounds = new(new(0), new(0), new(400), new(200));

        GeometryRect visual = ElementGeometry.GetVisualBounds(bounds, 30_000);

        Assert.Equal((400 * Math.Cos(Math.PI / 6)) + (200 * Math.Sin(Math.PI / 6)), visual.Width, 9);
        Assert.Equal((400 * Math.Sin(Math.PI / 6)) + (200 * Math.Cos(Math.PI / 6)), visual.Height, 9);
        Assert.Equal(200, visual.X + (visual.Width / 2), 9);
        Assert.Equal(100, visual.Y + (visual.Height / 2), 9);
    }

    [Fact]
    public void InverseRotation_MapsVisualPointBackToUnrotatedSpace()
    {
        MicrometreRect bounds = new(new(100), new(200), new(400), new(200));
        GeometryPoint rotatedTopLeft = ElementGeometry.GetRotatedCorners(bounds, 33_000)[0];

        GeometryPoint result = ElementGeometry.InverseRotatePoint(rotatedTopLeft, bounds, 33_000);

        AssertPoint(result, 100, 200);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    [InlineData(1.49, 1)]
    public void RoundToMicrometre_UsesAwayFromZero(double value, int expected) =>
        Assert.Equal(expected, ElementGeometry.RoundToMicrometre(value).Value);

    private static void AssertPoint(GeometryPoint actual, double expectedX, double expectedY)
    {
        Assert.Equal(expectedX, actual.X, 9);
        Assert.Equal(expectedY, actual.Y, 9);
    }
}
