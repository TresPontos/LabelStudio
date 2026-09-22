using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor.Tests;

public class ViewRotationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DocumentToCanvas_RoundTrip_ExactAtAllRotations(int rotation)
    {
        CanvasTransform view = new()
        {
            Zoom = 2.5,
            OffsetX = 100,
            OffsetY = 200,
            ViewRotationDegrees = rotation,
        };

        MicrometrePoint original = new(new(12345), new(67890));
        (double cx, double cy) = view.DocumentToCanvas(original);
        MicrometrePoint back = view.CanvasToDocument(cx, cy);

        Assert.Equal(original.X, back.X);
        Assert.Equal(original.Y, back.Y);
    }

    [Fact]
    public void Rotation_90_SwapsXY()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 90, Zoom = 1.0 };
        (double x, double y) = view.DocumentToCanvas(new(new(10000), new(0)));
        Assert.Equal(0, x, 1);
        Assert.Equal(30, y, 1);
    }

    [Fact]
    public void Rotation_180_NegatesBoth()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 180, Zoom = 1.0 };
        (double x, double y) = view.DocumentToCanvas(new(new(10000), new(5000)));
        Assert.Equal(-30, x, 1);
        Assert.Equal(-15, y, 1);
    }

    [Fact]
    public void Rotation_270_SwapsAndNegates()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 270, Zoom = 1.0 };
        (double x, double y) = view.DocumentToCanvas(new(new(10000), new(0)));
        Assert.Equal(0, x, 1);
        Assert.Equal(-30, y, 1);
    }

    [Fact]
    public void ZoomAtPoint_PreservesDocumentPoint_WhileRotated()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 90, Zoom = 1.0, OffsetX = 50, OffsetY = 50 };
        double screenX = 200, screenY = 300;

        MicrometrePoint before = view.CanvasToDocument(screenX, screenY);
        view.ZoomAtPoint(4.0, screenX, screenY);
        MicrometrePoint after = view.CanvasToDocument(screenX, screenY);

        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ZoomAtPoint_PreservesDocumentPoint_AtEveryRotatedAngle(int rotation)
    {
        CanvasTransform view = new()
        {
            ViewRotationDegrees = rotation,
            Zoom = 1.75,
            OffsetX = -35,
            OffsetY = 82,
        };
        view.SetContentSize(new(17_000), new(54_000));

        MicrometrePoint before = view.CanvasToDocument(321.5, 147.25);
        view.ZoomAtPoint(3.25, 321.5, 147.25);
        MicrometrePoint after = view.CanvasToDocument(321.5, 147.25);

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void FitToViewport_CentresRotatedBounds(int rotation)
    {
        CanvasTransform view = new() { ViewRotationDegrees = rotation };

        view.FitToViewport(new(17_000), new(54_000), 800, 500, 40);

        (double width, double height) = view.GetRotatedDisplaySize(17_000, 54_000);
        Assert.Equal((800 - width) / 2, view.OffsetX, 6);
        Assert.Equal((500 - height) / 2, view.OffsetY, 6);
        Assert.True(width <= 720.000001);
        Assert.True(height <= 420.000001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void FitToViewportTopLeft_AnchorsRotatedBoundsAtPadding(int rotation)
    {
        CanvasTransform view = new() { ViewRotationDegrees = rotation };

        view.FitToViewportTopLeft(new(17_000), new(54_000), 800, 500, 32);

        CanvasDisplayBounds bounds = view.GetDocumentDisplayBounds();
        Assert.Equal(32, bounds.Left);
        Assert.Equal(32, bounds.Top);
        Assert.True(bounds.Right <= 768.000001);
        Assert.True(bounds.Bottom <= 468.000001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void MultiplePositions_RoundTripWithinOneMicrometre(int rotation)
    {
        CanvasTransform view = new()
        {
            ViewRotationDegrees = rotation,
            Zoom = 3.7,
            OffsetX = 41.25,
            OffsetY = -18.75,
        };
        view.SetContentSize(new(54_000), new(17_000));

        foreach (MicrometrePoint point in new[]
        {
            new MicrometrePoint(new(0), new(0)),
            new MicrometrePoint(new(1), new(1)),
            new MicrometrePoint(new(12_345), new(6_789)),
            new MicrometrePoint(new(54_000), new(17_000)),
        })
        {
            (double x, double y) = view.DocumentToCanvas(point);
            MicrometrePoint result = view.CanvasToDocument(x, y);
            Assert.InRange(Math.Abs(result.X.Value - point.X.Value), 0, 1);
            Assert.InRange(Math.Abs(result.Y.Value - point.Y.Value), 0, 1);
        }
    }

    [Fact]
    public void ZoomAndPan_RoundTrip_WhileRotated()
    {
        CanvasTransform view = new()
        {
            ViewRotationDegrees = 90,
            Zoom = 3.0,
            OffsetX = -50,
            OffsetY = 75,
        };

        MicrometrePoint docPoint = new(new(42000), new(17000));
        (double cx, double cy) = view.DocumentToCanvas(docPoint);
        MicrometrePoint back = view.CanvasToDocument(cx, cy);

        Assert.Equal(docPoint.X, back.X);
        Assert.Equal(docPoint.Y, back.Y);
    }

    [Fact]
    public void ViewRotation_NormalizesToValidValues()
    {
        CanvasTransform view = new();
        view.ViewRotationDegrees = 450;
        Assert.Equal(90, view.ViewRotationDegrees);

        view.ViewRotationDegrees = -90;
        Assert.Equal(270, view.ViewRotationDegrees);

        CanvasTransform fresh = new();
        fresh.ViewRotationDegrees = 45;
        Assert.Equal(0, fresh.ViewRotationDegrees);
    }

    [Fact]
    public void GetRotatedDisplaySize_SwapsAt90And270()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 0, Zoom = 1.0 };
        (double w0, double h0) = view.GetRotatedDisplaySize(17000, 54000);
        Assert.Equal(51, w0, 1);
        Assert.Equal(162, h0, 1);

        view.ViewRotationDegrees = 90;
        (double w90, double h90) = view.GetRotatedDisplaySize(17000, 54000);
        Assert.Equal(162, w90, 1);
        Assert.Equal(51, h90, 1);
    }

    [Fact]
    public void ContentRotation_MapsDocumentIntoPositiveDisplayBounds()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 90, Zoom = 1.0 };
        view.SetContentSize(new(17000), new(54000));

        (double originX, double originY) = view.DocumentToCanvas(new(new(0), new(0)));
        (double farX, double farY) = view.DocumentToCanvas(new(new(17000), new(54000)));

        Assert.Equal(162, originX, 1);
        Assert.Equal(0, originY, 1);
        Assert.Equal(0, farX, 1);
        Assert.Equal(51, farY, 1);
        Assert.Equal(new Micrometre(0), view.CanvasToDocument(originX, originY).X);
        Assert.Equal(new Micrometre(17000), view.CanvasToDocument(farX, farY).X);
        Assert.Equal(new Micrometre(54000), view.CanvasToDocument(farX, farY).Y);
    }

    [Fact]
    public void IsRotated_TrueAt90And270()
    {
        CanvasTransform view = new();
        Assert.False(view.IsRotated);

        view.ViewRotationDegrees = 90;
        Assert.True(view.IsRotated);

        view.ViewRotationDegrees = 180;
        Assert.False(view.IsRotated);

        view.ViewRotationDegrees = 270;
        Assert.True(view.IsRotated);
    }

    [Fact]
    public void Reset_ClearsRotation()
    {
        CanvasTransform view = new() { ViewRotationDegrees = 90, Zoom = 5, OffsetX = 100 };
        view.Reset();
        Assert.Equal(0, view.ViewRotationDegrees);
        Assert.Equal(1.0, view.Zoom);
        Assert.Equal(0, view.OffsetX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DieCutPrintableArea_RemainsInsidePageAtEveryViewOrientation(int rotation)
    {
        CanvasTransform view = new() { ViewRotationDegrees = rotation };
        view.SetContentSize(new(17_000), new(53_900));

        (double left, double top, double right, double bottom) page = ScreenBounds(
            view, new MicrometreRect(Micrometre.Zero, Micrometre.Zero, new(17_000), new(53_900)));
        (double left, double top, double right, double bottom) printable = ScreenBounds(
            view, new MicrometreRect(new(1_500), new(3_000), new(14_000), new(47_900)));

        Assert.True(printable.left >= page.left - 0.01);
        Assert.True(printable.top >= page.top - 0.01);
        Assert.True(printable.right <= page.right + 0.01);
        Assert.True(printable.bottom <= page.bottom + 0.01);
    }

    private static (double Left, double Top, double Right, double Bottom) ScreenBounds(
        CanvasTransform view,
        MicrometreRect bounds)
    {
        (double X, double Y)[] corners =
        [
            view.DocumentToCanvas(bounds.X, bounds.Y),
            view.DocumentToCanvas(bounds.Right, bounds.Y),
            view.DocumentToCanvas(bounds.Right, bounds.Bottom),
            view.DocumentToCanvas(bounds.X, bounds.Bottom),
        ];
        return (
            corners.Min(point => point.X),
            corners.Min(point => point.Y),
            corners.Max(point => point.X),
            corners.Max(point => point.Y));
    }
}
