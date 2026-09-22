using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Layout;
using LabelStudio.Rendering;

namespace LabelStudio.Desktop.Tests;

public class ViewRotationPrintInvariantTests
{
    [Fact]
    public void CanvasViewRotation_DoesNotChangePreparedSceneOrThermalPlanes()
    {
        PhysicalSize size = PhysicalSize.FromMillimetres(17, 54);
        MicrometreRect printable = new(new(1_500), new(3_000), new(14_000), new(48_000));
        LabelDocument document = LabelDocument.Create(
            size,
            "test",
            new MediaSnapshot("test", size, printable),
            [
                new TextElement(
                    "text",
                    new(new(2_000), new(5_000), new(12_000), new(8_000)),
                    InkChannel.Black,
                    "VIEW INVARIANT",
                    18,
                    "Segoe UI"),
            ]);
        PreparedScene expectedScene = new LayoutEngine().Prepare(document);
        RenderTarget target = new(
            300,
            300,
            720,
            640,
            printable,
            555,
            165,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", true)]);
        RenderedPlanes expected = new ThermalTargetRenderer().Render(expectedScene, target);

        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            CanvasTransform view = new() { ViewRotationDegrees = rotation };
            view.SetContentSize(size.Width, size.Height);

            PreparedScene actualScene = new LayoutEngine().Prepare(document);
            RenderedPlanes actual = new ThermalTargetRenderer().Render(actualScene, target);

            Assert.Equal(expectedScene.LabelSize, actualScene.LabelSize);
            Assert.Equal(expectedScene.PrintableArea, actualScene.PrintableArea);
            Assert.Equal(expectedScene.Elements.ToArray(), actualScene.Elements.ToArray());
            Assert.Equal(expected.BlackPlane.Width, actual.BlackPlane.Width);
            Assert.Equal(expected.BlackPlane.Height, actual.BlackPlane.Height);
            Assert.True(expected.BlackPlane.Data.Span.SequenceEqual(actual.BlackPlane.Data.Span));
            Assert.True(expected.RedPlane!.Data.Span.SequenceEqual(actual.RedPlane!.Data.Span));
            Assert.Equal(rotation, view.ViewRotationDegrees);
            Assert.All(document.Elements, element => Assert.Equal(0, element.RotationMillidegrees));
        }
    }

    [Fact]
    public void SafeMarginsAndGridGeometryDoNotChangeThermalOutput()
    {
        PhysicalSize size = PhysicalSize.FromMillimetres(62, 100);
        MicrometreRect printable = new(new(1_500), new(3_000), new(58_900), new(94_000));
        LabelDocument document = LabelDocument.Create(
            size,
            "test",
            new MediaSnapshot("test", size, printable),
            [RectangleElement.Create("rect", new(new(5_000), new(5_000), new(10_000), new(10_000)), fill: true)],
            mediaKind: DocumentMediaKind.Continuous);
        RenderTarget target = new(
            300, 300, 720, 500, printable, 12, 696,
            [new InkOutputChannel("Black", false)]);
        PreparedScene baselineScene = new LayoutEngine().Prepare(document);
        MonochromeRaster baseline = new ThermalTargetRenderer().Render(baselineScene, target).BlackPlane;
        DocumentDesignMetadata changedMetadata = new(
            document.DesignMetadata.Groups,
            document.DesignMetadata.Guides,
            new DocumentGridGeometry(new(5_000), new(5_000), new(new(500), new(500)), 5),
            DocumentSafeMargins.Uniform(new(7_000)));
        LabelDocument changed = document with { DesignMetadata = changedMetadata };

        PreparedScene changedScene = new LayoutEngine().Prepare(changed);
        MonochromeRaster actual = new ThermalTargetRenderer().Render(changedScene, target).BlackPlane;

        Assert.Equal(baselineScene.LabelSize, changedScene.LabelSize);
        Assert.Equal(baselineScene.PrintableArea, changedScene.PrintableArea);
        Assert.Equal(baselineScene.Elements.ToArray(), changedScene.Elements.ToArray());
        Assert.True(baseline.Data.Span.SequenceEqual(actual.Data.Span));
    }
}
