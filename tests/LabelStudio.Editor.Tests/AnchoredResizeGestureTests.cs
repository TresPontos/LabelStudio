using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Transforms;

namespace LabelStudio.Editor.Tests;

public class AnchoredResizeGestureTests
{
    private static readonly MicrometreRect Original = new(new(1000), new(2000), new(8000), new(4000));

    [Theory]
    [InlineData(ResizeAnchor.TopLeft)]
    [InlineData(ResizeAnchor.Top)]
    [InlineData(ResizeAnchor.TopRight)]
    [InlineData(ResizeAnchor.Right)]
    [InlineData(ResizeAnchor.BottomRight)]
    [InlineData(ResizeAnchor.Bottom)]
    [InlineData(ResizeAnchor.BottomLeft)]
    [InlineData(ResizeAnchor.Left)]
    public void PointerDownAtHandle_DoesNotJump(ResizeAnchor anchor)
    {
        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(Original, 37_000, anchor);
        AnchoredResizeGesture gesture = new(Original, 37_000, anchor, handle);

        MicrometreRect result = gesture.Update(handle, 100, 100, proportional: false);

        Assert.Equal(Original, result);
    }

    [Theory]
    [InlineData(ResizeAnchor.TopLeft)]
    [InlineData(ResizeAnchor.Top)]
    [InlineData(ResizeAnchor.TopRight)]
    [InlineData(ResizeAnchor.Right)]
    [InlineData(ResizeAnchor.BottomRight)]
    [InlineData(ResizeAnchor.Bottom)]
    [InlineData(ResizeAnchor.BottomLeft)]
    [InlineData(ResizeAnchor.Left)]
    public void RotatedResize_KeepsOppositeAnchorFixed(ResizeAnchor anchor)
    {
        const int rotation = 37_000;
        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(Original, rotation, anchor);
        GeometryPoint fixedPoint = AnchoredResizeGesture.GetHandlePoint(Original, rotation, Opposite(anchor));
        AnchoredResizeGesture gesture = new(Original, rotation, anchor, handle);

        MicrometreRect result = gesture.Update(
            new GeometryPoint(handle.X + 1700, handle.Y - 900),
            100,
            100,
            proportional: false);
        GeometryPoint resultFixedPoint = AnchoredResizeGesture.GetHandlePoint(result, rotation, Opposite(anchor));

        Assert.InRange(Math.Abs(resultFixedPoint.X - fixedPoint.X), 0, 1.5);
        Assert.InRange(Math.Abs(resultFixedPoint.Y - fixedPoint.Y), 0, 1.5);
    }

    [Fact]
    public void CornerResize_DraggedHandleTracksPointerWithGrabOffset()
    {
        const int rotation = -28_000;
        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(Original, rotation, ResizeAnchor.BottomRight);
        GeometryPoint pointerDown = new(handle.X + 13, handle.Y - 9);
        AnchoredResizeGesture gesture = new(Original, rotation, ResizeAnchor.BottomRight, pointerDown);
        GeometryPoint pointer = new(pointerDown.X + 2200, pointerDown.Y + 700);

        MicrometreRect result = gesture.Update(pointer, 100, 100, proportional: false);
        GeometryPoint resultHandle = AnchoredResizeGesture.GetHandlePoint(result, rotation, ResizeAnchor.BottomRight);

        Assert.InRange(Math.Abs(resultHandle.X - (pointer.X - 13)), 0, 1.5);
        Assert.InRange(Math.Abs(resultHandle.Y - (pointer.Y + 9)), 0, 1.5);
    }

    [Fact]
    public void RepeatedUpdates_AreAlwaysCalculatedFromOriginalGeometry()
    {
        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(Original, 45_000, ResizeAnchor.BottomRight);
        GeometryPoint finalPointer = new(handle.X + 3000, handle.Y + 1200);
        AnchoredResizeGesture direct = new(Original, 45_000, ResizeAnchor.BottomRight, handle);
        AnchoredResizeGesture repeated = new(Original, 45_000, ResizeAnchor.BottomRight, handle);

        MicrometreRect expected = direct.Update(finalPointer, 100, 100, proportional: false);
        _ = repeated.Update(new GeometryPoint(handle.X + 500, handle.Y + 200), 100, 100, proportional: false);
        _ = repeated.Update(new GeometryPoint(handle.X - 800, handle.Y + 400), 100, 100, proportional: false);
        MicrometreRect actual = repeated.Update(finalPointer, 100, 100, proportional: false);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProportionalCornerResize_PreservesAspectRatio()
    {
        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(Original, 20_000, ResizeAnchor.BottomRight);
        AnchoredResizeGesture gesture = new(Original, 20_000, ResizeAnchor.BottomRight, handle);

        MicrometreRect result = gesture.Update(
            new GeometryPoint(handle.X + 5000, handle.Y + 400),
            100,
            100,
            proportional: true);

        Assert.Equal(2, (double)result.Width.Value / result.Height.Value, 3);
    }

    private static ResizeAnchor Opposite(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => ResizeAnchor.BottomRight,
        ResizeAnchor.Top => ResizeAnchor.Bottom,
        ResizeAnchor.TopRight => ResizeAnchor.BottomLeft,
        ResizeAnchor.Right => ResizeAnchor.Left,
        ResizeAnchor.BottomRight => ResizeAnchor.TopLeft,
        ResizeAnchor.Bottom => ResizeAnchor.Top,
        ResizeAnchor.BottomLeft => ResizeAnchor.TopRight,
        ResizeAnchor.Left => ResizeAnchor.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
    };
}
