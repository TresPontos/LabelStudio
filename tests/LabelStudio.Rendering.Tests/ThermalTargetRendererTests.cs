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

    private static bool AnyPixelSet(MonochromeRaster raster)
    {
        ReadOnlySpan<byte> data = raster.Data.Span;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0) return true;
        }
        return false;
    }
}