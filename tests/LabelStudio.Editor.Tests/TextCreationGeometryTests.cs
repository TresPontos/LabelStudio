using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Transforms;

namespace LabelStudio.Editor.Tests;

public class TextCreationGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ViewAlignedDrag_CreatesUprightFrameWithMatchingScreenBounds(int viewRotation)
    {
        CanvasTransform view = new()
        {
            ViewRotationDegrees = viewRotation,
            Zoom = 1,
            OffsetX = 80,
            OffsetY = 60,
        };
        view.SetContentSize(new(17_000), new(53_900));
        (double centerX, double centerY) = view.DocumentToCanvas(new MicrometrePoint(new(8_500), new(26_950)));
        const double desiredWidth = 30;
        const double desiredHeight = 12;
        MicrometrePoint start = view.CanvasToDocument(
            centerX - desiredWidth / 2,
            centerY - desiredHeight / 2);
        MicrometrePoint end = view.CanvasToDocument(
            centerX + desiredWidth / 2,
            centerY + desiredHeight / 2);
        MicrometreRect dragBounds = new(
            new(Math.Min(start.X.Value, end.X.Value)),
            new(Math.Min(start.Y.Value, end.Y.Value)),
            new(Math.Abs(end.X.Value - start.X.Value)),
            new(Math.Abs(end.Y.Value - start.Y.Value)));

        TextCreationFrame frame = TextCreationGeometry.FromViewAlignedDrag(dragBounds, viewRotation);
        (double left, double top, double right, double bottom) = ScreenBounds(
            view,
            frame.Bounds,
            frame.RotationMillidegrees);

        Assert.InRange(Math.Abs((right - left) - desiredWidth), 0, 0.02);
        Assert.InRange(Math.Abs((bottom - top) - desiredHeight), 0, 0.02);
        Assert.InRange(Math.Abs((left + right) / 2 - centerX), 0, 0.02);
        Assert.InRange(Math.Abs((top + bottom) / 2 - centerY), 0, 0.02);
        Assert.Equal(0, ElementGeometry.NormalizeAngle(
            frame.RotationMillidegrees + viewRotation * 1000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ManualQuarterTurn_RotatesFrameAndTextTogetherFromUprightBaseline(int viewRotation)
    {
        MicrometreRect dragBounds = viewRotation is 90 or 270
            ? new(new(1_000), new(2_000), new(4_000), new(10_000))
            : new(new(1_000), new(2_000), new(10_000), new(4_000));
        TextCreationFrame frame = TextCreationGeometry.FromViewAlignedDrag(dragBounds, viewRotation);

        int manuallyRotated = ElementGeometry.NormalizeAngle(frame.RotationMillidegrees + 90_000);

        Assert.Equal(90_000, ElementGeometry.NormalizeAngle(manuallyRotated + viewRotation * 1000));
    }

    [Fact]
    public void DefaultFontSize_IsDerivedFromFrameHeight()
    {
        TextCreationFrame shortFrame = TextCreationGeometry.FromViewAlignedDrag(
            new MicrometreRect(Micrometre.Zero, Micrometre.Zero, new(20_000), new(3_000)),
            0);
        TextCreationFrame tallFrame = TextCreationGeometry.FromViewAlignedDrag(
            new MicrometreRect(Micrometre.Zero, Micrometre.Zero, new(20_000), new(10_000)),
            0);

        Assert.InRange(shortFrame.FontSizePoints, 4, 8);
        Assert.True(tallFrame.FontSizePoints > shortFrame.FontSizePoints);
        Assert.True(tallFrame.FontSizePoints < 24);
    }

    private static (double Left, double Top, double Right, double Bottom) ScreenBounds(
        CanvasTransform view,
        MicrometreRect bounds,
        int rotationMillidegrees)
    {
        (double X, double Y)[] corners = ElementGeometry.GetRotatedCorners(bounds, rotationMillidegrees)
            .Select(point => view.DocumentToCanvas(
                new Micrometre((int)Math.Round(point.X, MidpointRounding.AwayFromZero)),
                new Micrometre((int)Math.Round(point.Y, MidpointRounding.AwayFromZero))))
            .ToArray();
        return (
            corners.Min(point => point.X),
            corners.Min(point => point.Y),
            corners.Max(point => point.X),
            corners.Max(point => point.Y));
    }
}
