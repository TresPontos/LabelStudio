using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;

namespace LabelStudio.Layout.Tests;

public class LayoutEngineTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)));
        return LabelDocument.Create(dims, "brother.dk-22251", snap, elements);
    }

    [Fact]
    public void Prepare_ProducesSceneWithCorrectLabelDimensions()
    {
        LabelDocument doc = CreateDoc();
        LayoutEngine engine = new();
        PreparedScene scene = engine.Prepare(doc);

        Assert.Equal(doc.PageDimensions, scene.LabelSize);
        Assert.Equal(doc.MediaGeometry.PrintableArea, scene.PrintableArea);
    }

    [Fact]
    public void Prepare_Rectangle_PreservesBoundsAndInk()
    {
        RectangleElement rect = RectangleElement.Create("r1",
            new(new(1000), new(2000), new(10000), new(5000)), InkChannel.Black, fill: true, strokeWidth: new(200));
        LabelDocument doc = CreateDoc(rect);

        PreparedScene scene = new LayoutEngine().Prepare(doc);
        PreparedElement pe = Assert.Single(scene.Elements);

        Assert.Equal("r1", pe.SourceElementId);
        Assert.Equal(rect.Bounds, pe.Bounds);
        Assert.Equal(InkChannel.Black, pe.Ink);
        Assert.True(pe.IsFilled);
        Assert.Equal(200, pe.StrokeWidth.Value);
        Assert.Equal(PreparedContentType.Rectangle, pe.Content.ContentType);
    }

    [Fact]
    public void Prepare_Line_PreservesThickness()
    {
        LineElement line = new("l1",
            new(new(0), new(0)), new(new(10000), new(0)),
            new(300), InkChannel.Red);
        LabelDocument doc = CreateDoc(line);

        PreparedScene scene = new LayoutEngine().Prepare(doc);
        PreparedElement pe = Assert.Single(scene.Elements);

        Assert.Equal(InkChannel.Red, pe.Ink);
        Assert.Equal(300, pe.LineThickness.Value);
        Assert.Equal(PreparedContentType.Line, pe.Content.ContentType);
    }

    [Fact]
    public void Prepare_Image_PreservesAssetId()
    {
        ImageElement img = new("i1",
            new(new(0), new(0), new(5000), new(5000)),
            InkChannel.Black, "asset-123");
        LabelDocument doc = CreateDoc(img);

        PreparedScene scene = new LayoutEngine().Prepare(doc);
        PreparedElement pe = Assert.Single(scene.Elements);

        Assert.Equal("asset-123", pe.Content.AssetId);
        Assert.Equal(PreparedContentType.Image, pe.Content.ContentType);
    }

    [Fact]
    public void Prepare_Text_PreservesText()
    {
        TextElement text = new("t1",
            new(new(0), new(0), new(20000), new(3000)),
            InkChannel.Black, "Hello World", 24, null);
        LabelDocument doc = CreateDoc(text);

        PreparedScene scene = new LayoutEngine().Prepare(doc);
        PreparedElement pe = Assert.Single(scene.Elements);

        Assert.Equal("Hello World", pe.Content.TextContent);
        Assert.Equal(PreparedContentType.Text, pe.Content.ContentType);
    }

    [Fact]
    public void Prepare_MultipleElements_PreservesOrder()
    {
        RectangleElement r1 = RectangleElement.Create("r1", MicrometreRect.Zero);
        RectangleElement r2 = RectangleElement.Create("r2", MicrometreRect.Zero);
        LabelDocument doc = CreateDoc(r1, r2);

        PreparedScene scene = new LayoutEngine().Prepare(doc);

        Assert.Equal(2, scene.Elements.Count);
        Assert.Equal("r1", scene.Elements[0].SourceElementId);
        Assert.Equal("r2", scene.Elements[1].SourceElementId);
    }

    [Fact]
    public void Prepare_EmptyDocument_ProducesEmptyScene()
    {
        LabelDocument doc = CreateDoc();
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        Assert.Empty(scene.Elements);
    }
}