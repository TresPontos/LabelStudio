using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Tests;

public class CanvasTransformTests
{
    [Fact]
    public void DocumentToCanvas_RoundTrip_StaysExact()
    {
        CanvasTransform view = new() { Zoom = 2.5, OffsetX = 100, OffsetY = 200 };

        MicrometrePoint original = new(new(12345), new(67890));
        (double cx, double cy) = view.DocumentToCanvas(original);
        MicrometrePoint back = view.CanvasToDocument(cx, cy);

        Assert.Equal(original.X, back.X);
        Assert.Equal(original.Y, back.Y);
    }

    [Fact]
    public void Zoom_DoesNotModifyDocumentGeometry()
    {
        CanvasTransform view = new();
        MicrometrePoint docPoint = new(new(10000), new(20000));

        view.Zoom = 1.0;
        (double x1, double y1) = view.DocumentToCanvas(docPoint);
        MicrometrePoint back1 = view.CanvasToDocument(x1, y1);

        view.Zoom = 8.0;
        (double x8, double y8) = view.DocumentToCanvas(docPoint);
        MicrometrePoint back8 = view.CanvasToDocument(x8, y8);

        Assert.Equal(docPoint.X, back1.X);
        Assert.Equal(docPoint.Y, back1.Y);
        Assert.Equal(docPoint.X, back8.X);
        Assert.Equal(docPoint.Y, back8.Y);
    }

    [Fact]
    public void Pan_DoesNotModifyDocumentGeometry()
    {
        CanvasTransform view = new();
        MicrometrePoint docPoint = new(new(5000), new(8000));

        view.OffsetX = 300;
        view.OffsetY = -100;

        (double cx, double cy) = view.DocumentToCanvas(docPoint);
        MicrometrePoint back = view.CanvasToDocument(cx, cy);

        Assert.Equal(docPoint.X, back.X);
        Assert.Equal(docPoint.Y, back.Y);
    }

    [Fact]
    public void ZoomAtPoint_PreservesDocumentPointUnderCursor()
    {
        CanvasTransform view = new() { Zoom = 1.0, OffsetX = 50, OffsetY = 50 };
        double screenX = 200, screenY = 300;

        MicrometrePoint before = view.CanvasToDocument(screenX, screenY);
        view.ZoomAtPoint(4.0, screenX, screenY);
        MicrometrePoint after = view.CanvasToDocument(screenX, screenY);

        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
    }

    [Fact]
    public void Zoom_ClampsToRange()
    {
        CanvasTransform view = new();
        view.Zoom = 0.01;
        Assert.Equal(CanvasTransform.MinZoom, view.Zoom);

        view.Zoom = 100;
        Assert.Equal(CanvasTransform.MaxZoom, view.Zoom);
    }

    [Fact]
    public void ScreenToleranceToDocument_ScalesWithZoom()
    {
        CanvasTransform view = new();

        view.Zoom = 1.0;
        double tol100 = view.ScreenToleranceToDocument(7.0).Value;

        view.Zoom = 10.0;
        double tol1000 = view.ScreenToleranceToDocument(7.0).Value;

        Assert.True(tol1000 < tol100);
        Assert.True(tol1000 > 0);
    }
}