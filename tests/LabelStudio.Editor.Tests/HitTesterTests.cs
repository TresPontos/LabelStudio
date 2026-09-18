using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Tests;

public class HitTesterTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62, 0);
        MediaSnapshot snap = new("test", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "test", snap, elements);
    }

    [Fact]
    public void HitTest_Rectangle_HitInside()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        string? hit = HitTester.HitTestTopmost(doc, new(new(2000), new(2000)), new(0));
        Assert.Equal("r1", hit);
    }

    [Fact]
    public void HitTest_Rectangle_MissOutside()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        string? hit = HitTester.HitTestTopmost(doc, new(new(9000), new(9000)), new(0));
        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_Line_WithTolerance()
    {
        LineElement line = new("l1",
            new(new(0), new(5000)),
            new(new(10000), new(5000)),
            new(200), InkChannel.Black);
        LabelDocument doc = CreateDoc(line);

        string? hit = HitTester.HitTestTopmost(doc, new(new(5000), new(5200)), new(300));
        Assert.Equal("l1", hit);

        string? miss = HitTester.HitTestTopmost(doc, new(new(5000), new(6000)), new(300));
        Assert.Null(miss);
    }

    [Fact]
    public void HitTest_Overlapping_ReturnsTopmost()
    {
        RectangleElement bottom = RectangleElement.Create("r1", new(new(0), new(0), new(10000), new(10000)));
        RectangleElement top = RectangleElement.Create("r2", new(new(0), new(0), new(10000), new(10000)));
        LabelDocument doc = CreateDoc(bottom, top);

        string? hit = HitTester.HitTestTopmost(doc, new(new(5000), new(5000)), new(0));
        Assert.Equal("r2", hit);
    }

    [Fact]
    public void HitTest_ToleranceScalesWithZoom()
    {
        CanvasTransform view = new();

        view.Zoom = 1.0;
        Micrometre tolAt100 = view.ScreenToleranceToDocument(7.0);

        view.Zoom = 4.0;
        Micrometre tolAt400 = view.ScreenToleranceToDocument(7.0);

        Assert.True(tolAt400 < tolAt100,
            $"Tolerance at 400% zoom ({tolAt400}) should be smaller than at 100% ({tolAt100}).");
    }

    [Fact]
    public void HitTest_ExcludedIds_SkipsElements()
    {
        RectangleElement r1 = RectangleElement.Create("r1", new(new(0), new(0), new(10000), new(10000)));
        RectangleElement r2 = RectangleElement.Create("r2", new(new(0), new(0), new(10000), new(10000)));
        LabelDocument doc = CreateDoc(r1, r2);

        string? hit = HitTester.HitTestTopmost(doc, new(new(5000), new(5000)), new(0), new HashSet<string> { "r2" });
        Assert.Equal("r1", hit);
    }
}