using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using LabelStudio.Rendering;

namespace LabelStudio.Rendering.Tests;

public class ThermalTargetRendererTests
{
    private static RenderTarget CreateTarget(int width = 720, int height = 500, bool red = false)
    {
        List<InkOutputChannel> channels = [new("Black", false)];
        if (red) channels.Add(new("Red", true));

        return new RenderTarget(
            300, 300, width, height,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)),
            12, 696, channels);
    }

    private static PreparedScene SceneWithElements(params PreparedElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MicrometreRect printable = new(new(1500), new(0), new(58900), new(0));
        return new PreparedScene(dims, printable, elements);
    }

    [Fact]
    public void Render_ProducesCorrectDimensions()
    {
        RenderTarget target = CreateTarget(720, 200);
        PreparedScene scene = SceneWithElements();

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        Assert.Equal(720, planes.BlackPlane.Width);
        Assert.Equal(200, planes.BlackPlane.Height);
    }

    [Fact]
    public void Render_FilledRectangle_SetsPixelsInBounds()
    {
        RenderTarget target = CreateTarget(720, 500);
        PreparedElement rect = new("r1",
            new MicrometreRect(new(5000), new(5000), new(10000), new(5000)),
            InkChannel.Black,
            PreparedContent.Rectangle())
        { IsFilled = true };

        PreparedScene scene = SceneWithElements(rect);
        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        int headOffset = 12;
        int dotX = PhysicalUnits.MicrometresToDots(5000, 300) + headOffset;
        int dotY = PhysicalUnits.MicrometresToDots(5000, 300);
        int dotW = PhysicalUnits.MicrometresToDots(10000, 300);
        int dotH = PhysicalUnits.MicrometresToDots(5000, 300);

        Assert.True(planes.BlackPlane.GetPixel(dotX, dotY));
        Assert.True(planes.BlackPlane.GetPixel(dotX + dotW - 1, dotY + dotH - 1));
        Assert.True(planes.BlackPlane.GetPixel(dotX + dotW / 2, dotY + dotH / 2));
    }

    [Fact]
    public void Render_ClipsPixelsOutsidePrintableHeadWindow()
    {
        RenderTarget target = CreateTarget(720, 100);
        PreparedElement rect = new("r1",
            new MicrometreRect(new(58_000), new(5000), new(6000), new(5000)),
            InkChannel.Black,
            PreparedContent.Rectangle())
        { IsFilled = true };

        RenderedPlanes planes = new ThermalTargetRenderer().Render(SceneWithElements(rect), target);

        Assert.True(planes.BlackPlane.GetPixel(707, 59));
        Assert.False(planes.BlackPlane.GetPixel(708, 59));
        Assert.False(planes.BlackPlane.GetPixel(719, 59));
    }

    [Fact]
    public void Render_RedInk_WritesToRedPlane()
    {
        RenderTarget target = CreateTarget(720, 100, red: true);
        PreparedElement redRect = new("r1",
            new MicrometreRect(new(5000), new(5000), new(10000), new(5000)),
            InkChannel.Red,
            PreparedContent.Rectangle())
        { IsFilled = true };

        PreparedScene scene = SceneWithElements(redRect);
        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        Assert.NotNull(planes.RedPlane);
        int headOffset = 12;
        int dotX = PhysicalUnits.MicrometresToDots(5000, 300) + headOffset;
        int dotY = PhysicalUnits.MicrometresToDots(5000, 300);
        Assert.True(planes.RedPlane!.GetPixel(dotX, dotY));
    }

    [Fact]
    public void Render_TransparentInk_WritesToNoPlane()
    {
        RenderTarget target = CreateTarget(720, 100, red: true);
        PreparedElement transparent = new("t1",
            new MicrometreRect(new(0), new(0), new(10000), new(10000)),
            InkChannel.Transparent,
            PreparedContent.Rectangle())
        { IsFilled = true };

        PreparedScene scene = SceneWithElements(transparent);
        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        Assert.False(AnyPixelSet(planes.BlackPlane));
        Assert.True(planes.RedPlane is null || !AnyPixelSet(planes.RedPlane));
    }

    [Fact]
    public void Render_EmptyScene_ProducesBlankPlanes()
    {
        RenderTarget target = CreateTarget(720, 200);
        PreparedScene scene = SceneWithElements();

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        Assert.False(AnyPixelSet(planes.BlackPlane));
    }

    [Fact]
    public void Render_NoRedTarget_ProducesNoRedPlane()
    {
        RenderTarget target = CreateTarget(720, 100, red: false);
        PreparedElement redElement = new("r1",
            new MicrometreRect(new(0), new(0), new(10000), new(10000)),
            InkChannel.Red,
            PreparedContent.Rectangle())
        { IsFilled = true };

        PreparedScene scene = SceneWithElements(redElement);
        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);

        Assert.Null(planes.RedPlane);
    }

    [Fact]
    public void Render_ChannelMarkedAsNotRed_DoesNotCreateRedPlane()
    {
        RenderTarget target = new(
            300, 300, 720, 100,
            new MicrometreRect(new(1500), new(0), new(14_000), new(47_900)),
            555, 165,
            [new("Black", false), new("Red", false)]);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(SceneWithElements(), target);

        Assert.Null(planes.RedPlane);
    }

    [Fact]
    public void Render_TextUsesActualGlyphShapes()
    {
        RenderTarget target = CreateTarget(720, 200);
        MicrometreRect bounds = new(new(5000), new(5000), new(30_000), new(10_000));
        PreparedElement letterA = new("a", bounds, InkChannel.Black, PreparedContent.Text("A"))
        {
            TextFontSizePoints = 36,
            TextFontFamily = "Segoe UI",
        };
        PreparedElement letterB = letterA with
        {
            SourceElementId = "b",
            Content = PreparedContent.Text("B"),
        };

        MonochromeRaster a = new ThermalTargetRenderer().Render(SceneWithElements(letterA), target).BlackPlane;
        MonochromeRaster b = new ThermalTargetRenderer().Render(SceneWithElements(letterB), target).BlackPlane;

        Assert.True(AnyPixelSet(a));
        Assert.True(AnyPixelSet(b));
        Assert.False(a.Data.Span.SequenceEqual(b.Data.Span));
    }

    [Fact]
    public void Render_TextRotationRotatesPrintedGlyphs()
    {
        RenderTarget target = CreateTarget(720, 500);
        PreparedElement horizontal = new(
            "text",
            new(new(10_000), new(10_000), new(35_000), new(12_000)),
            InkChannel.Black,
            PreparedContent.Text("WIDE"))
        {
            TextFontSizePoints = 36,
            TextFontFamily = "Segoe UI",
        };
        PreparedElement vertical = horizontal with { RotationMillidegrees = 90_000 };

        MonochromeRaster horizontalRaster = new ThermalTargetRenderer()
            .Render(SceneWithElements(horizontal), target).BlackPlane;
        MonochromeRaster verticalRaster = new ThermalTargetRenderer()
            .Render(SceneWithElements(vertical), target).BlackPlane;
        (int horizontalWidth, int horizontalHeight) = InkDimensions(horizontalRaster);
        (int verticalWidth, int verticalHeight) = InkDimensions(verticalRaster);

        Assert.True(horizontalWidth > horizontalHeight);
        Assert.True(verticalHeight > verticalWidth);
    }

    [Fact]
    public void Render_MultilineTextDrawsBothLines()
    {
        RenderTarget target = CreateTarget(720, 300);
        PreparedElement firstLine = new(
            "single",
            new(new(5_000), new(5_000), new(30_000), new(20_000)),
            InkChannel.Black,
            PreparedContent.Text("TOP"))
        {
            TextFontSizePoints = 24,
            TextFontFamily = "Segoe UI",
        };
        PreparedElement twoLines = firstLine with
        {
            SourceElementId = "multiline",
            Content = PreparedContent.Text("TOP\nBOTTOM"),
        };

        MonochromeRaster single = new ThermalTargetRenderer().Render(SceneWithElements(firstLine), target).BlackPlane;
        MonochromeRaster multiline = new ThermalTargetRenderer().Render(SceneWithElements(twoLines), target).BlackPlane;

        Assert.True(InkDimensions(multiline).Height > InkDimensions(single).Height);
    }

    [Fact]
    public void Render_DocumentOriginMapsPrintableStartToRasterOrigin()
    {
        RenderTarget target = CreateTarget(720, 100) with
        {
            DocumentOriginX = new Micrometre(5_000),
            DocumentOriginY = new Micrometre(3_000),
        };
        PreparedElement rectangle = new(
            "rect",
            new(new(5_000), new(3_000), new(5_000), new(2_000)),
            InkChannel.Black,
            PreparedContent.Rectangle())
        {
            IsFilled = true,
        };

        MonochromeRaster raster = new ThermalTargetRenderer()
            .Render(SceneWithElements(rectangle), target).BlackPlane;

        Assert.True(raster.GetPixel(12, 0));
    }

    private static bool AnyPixelSet(MonochromeRaster raster)
    {
        ReadOnlySpan<byte> data = raster.Data.Span;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0) return true;
        }
        return false;
    }

    private static (int Width, int Height) InkDimensions(MonochromeRaster raster)
    {
        int left = raster.Width;
        int top = raster.Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < raster.Height; y++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                if (!raster.GetPixel(x, y)) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left ? (0, 0) : (right - left + 1, bottom - top + 1);
    }
}
