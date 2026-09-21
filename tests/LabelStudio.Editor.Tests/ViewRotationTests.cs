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
}