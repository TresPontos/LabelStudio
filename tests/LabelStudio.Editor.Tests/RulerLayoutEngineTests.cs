using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Tests;

public class RulerLayoutEngineTests
{
    private readonly RulerLayoutEngine _engine = new();

    [Fact]
    public void OriginAlignsWithTransformedDocumentTopLeft()
    {
        CanvasTransform transform = CreateTransform(0, 2, 37, 19, 100_000, 50_000);

        RulerLayout horizontal = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);
        RulerLayout vertical = _engine.CreateLayout(transform, RulerAxis.Vertical, 0, 1000);

        Assert.Equal(37, horizontal.Origin);
        Assert.Equal(19, vertical.Origin);
        Assert.Equal(37, Assert.Single(horizontal.Ticks, tick => tick.ValueMillimetres == 0).Position);
        Assert.Equal(19, Assert.Single(vertical.Ticks, tick => tick.ValueMillimetres == 0).Position);
    }

    [Fact]
    public void TickScaleAdaptsToZoom()
    {
        CanvasTransform transform = CreateTransform(0, 1, 0, 0, 1_000_000, 100_000);
        RulerLayout normal = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        transform.Zoom = 10;
        RulerLayout enlarged = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        Assert.Equal(20, normal.MajorStepMillimetres);
        Assert.Equal(4, normal.MinorStepMillimetres);
        Assert.Equal(2, enlarged.MajorStepMillimetres);
        Assert.Equal("20", normal.Ticks.Single(tick => tick.ValueMillimetres == 20).Label);
        Assert.Null(normal.Ticks.Single(tick => tick.ValueMillimetres == 4).Label);
        Assert.True(normal.MajorStepMillimetres * 3 >= RulerLayoutEngine.MinimumMajorTickSpacing);
        Assert.True(enlarged.MajorStepMillimetres * 30 >= RulerLayoutEngine.MinimumMajorTickSpacing);
    }

    [Fact]
    public void PanMovesTicksWithoutChangingValues()
    {
        CanvasTransform transform = CreateTransform(0, 2, 10, 0, 100_000, 50_000);
        RulerLayout before = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        transform.OffsetX = 35;
        RulerLayout after = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        RulerTick beforeZero = Assert.Single(before.Ticks, tick => tick.ValueMillimetres == 0);
        RulerTick afterZero = Assert.Single(after.Ticks, tick => tick.ValueMillimetres == 0);
        Assert.Equal(25, afterZero.Position - beforeZero.Position);
        Assert.Equal(before.MajorStepMillimetres, after.MajorStepMillimetres);
    }

    [Fact]
    public void ZoomChangesTickSpacingAndPreservesMillimetreValues()
    {
        CanvasTransform transform = CreateTransform(0, 1, 0, 0, 100_000, 50_000);
        RulerLayout before = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);
        double beforeTwentyMillimetres = before.Ticks.Single(tick => tick.ValueMillimetres == 20).Position;

        transform.Zoom = 2;
        RulerLayout after = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);
        double afterTwentyMillimetres = after.Ticks.Single(tick => tick.ValueMillimetres == 20).Position;

        Assert.Equal(beforeTwentyMillimetres * 2, afterTwentyMillimetres);
        Assert.Equal(100, after.ContentLengthMillimetres, 6);
    }

    [Theory]
    [InlineData(0, 80, 30)]
    [InlineData(90, 30, 80)]
    [InlineData(180, 80, 30)]
    [InlineData(270, 30, 80)]
    public void ValuesIncreaseAlongScreenAxesAtEveryRotation(
        int rotation,
        double horizontalLength,
        double verticalLength)
    {
        CanvasTransform transform = CreateTransform(rotation, 2, 23, 41, 80_000, 30_000);

        RulerLayout horizontal = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);
        RulerLayout vertical = _engine.CreateLayout(transform, RulerAxis.Vertical, 0, 1000);

        Assert.Equal(horizontalLength, horizontal.ContentLengthMillimetres, 6);
        Assert.Equal(verticalLength, vertical.ContentLengthMillimetres, 6);
        Assert.All(horizontal.Ticks.Zip(horizontal.Ticks.Skip(1)), pair =>
        {
            Assert.True(pair.First.Position < pair.Second.Position);
            Assert.True(pair.First.ValueMillimetres < pair.Second.ValueMillimetres);
        });
        Assert.All(vertical.Ticks.Zip(vertical.Ticks.Skip(1)), pair =>
        {
            Assert.True(pair.First.Position < pair.Second.Position);
            Assert.True(pair.First.ValueMillimetres < pair.Second.ValueMillimetres);
        });
    }

    [Fact]
    public void ChangedContinuousLengthUpdatesRulerExtent()
    {
        CanvasTransform transform = CreateTransform(0, 1, 0, 0, 62_000, 20_000);
        RulerLayout before = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        transform.SetContentSize(new(147_000), new(20_000));
        RulerLayout after = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 1000);

        Assert.Equal(62, before.ContentLengthMillimetres, 6);
        Assert.Equal(147, after.ContentLengthMillimetres, 6);
        Assert.True(after.Ticks.Max(tick => tick.ValueMillimetres) > before.Ticks.Max(tick => tick.ValueMillimetres));
    }

    [Fact]
    public void ViewportClipsTicksAndCursorMarkerUsesRulerSemantics()
    {
        CanvasTransform transform = CreateTransform(0, 1, -30, 0, 100_000, 20_000);

        RulerLayout layout = _engine.CreateLayout(transform, RulerAxis.Horizontal, 0, 100);
        RulerCursorMarker marker = Assert.IsType<RulerCursorMarker>(
            _engine.CreateCursorMarker(transform, RulerAxis.Horizontal, 45));

        Assert.All(layout.Ticks, tick => Assert.InRange(tick.Position, 0, 100));
        Assert.Equal(25, marker.ValueMillimetres, 6);
        Assert.Null(_engine.CreateCursorMarker(transform, RulerAxis.Horizontal, -31));
    }

    private static CanvasTransform CreateTransform(
        int rotation,
        double zoom,
        double offsetX,
        double offsetY,
        int width,
        int height)
    {
        CanvasTransform transform = new()
        {
            ViewRotationDegrees = rotation,
            Zoom = zoom,
            OffsetX = offsetX,
            OffsetY = offsetY,
        };
        transform.SetContentSize(new(width), new(height));
        return transform;
    }
}
