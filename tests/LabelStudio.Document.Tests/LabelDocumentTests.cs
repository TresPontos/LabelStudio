using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document.Tests;

public class LabelDocumentTests
{
    [Fact]
    public void Create_AssignsNewIdAndCurrentVersion()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-22251", snap);

        Assert.NotEqual(Guid.Empty, doc.Id.Value);
        Assert.Equal(2, doc.FormatVersion);
        Assert.Equal(dims, doc.PageDimensions);
        Assert.Equal("brother.dk-22251", doc.MediaProfileId);
        Assert.Empty(doc.Elements);
        Assert.Empty(doc.DesignMetadata.Groups);
        Assert.Empty(doc.DesignMetadata.Guides);
        Assert.Equal(new Micrometre(1000), doc.DesignMetadata.Grid.XSpacing);
        Assert.Equal(new Micrometre(1000), doc.DesignMetadata.Grid.YSpacing);
        Assert.Equal(MicrometrePoint.Zero, doc.DesignMetadata.Grid.Origin);
        Assert.Equal(10, doc.DesignMetadata.Grid.MajorInterval);
    }

    [Fact]
    public void Elements_HaveV2DesignDefaults()
    {
        RectangleElement rectangle = RectangleElement.Create("r1", MicrometreRect.Zero);
        ImageElement image = new("i1", MicrometreRect.Zero, InkChannel.Black, "image.bin");

        Assert.Null(rectangle.Name);
        Assert.True(rectangle.IsVisible);
        Assert.False(rectangle.IsLocked);
        Assert.Equal(0, rectangle.RotationMillidegrees);
        Assert.True(image.LockAspectRatio);
    }

    [Fact]
    public void Create_WithElements_PreservesOrder()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        RectangleElement rect = RectangleElement.Create("r1", MicrometreRect.Zero);
        LineElement line = new("l1", MicrometrePoint.Zero, new(new(1000), new(500)), new(20), InkChannel.Black);

        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-22251", snap, [rect, line]);

        Assert.Equal(2, doc.Elements.Count);
        Assert.IsType<RectangleElement>(doc.Elements[0]);
        Assert.IsType<LineElement>(doc.Elements[1]);
    }

    [Fact]
    public void WithElements_ReturnsNewDocument()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-22251", snap);

        RectangleElement rect = RectangleElement.Create("r1", new(new(0), new(0), new(1000), new(500)));
        LabelDocument updated = doc.WithElements([rect]);

        Assert.Empty(doc.Elements);
        Assert.Single(updated.Elements);
        Assert.Same(doc.Id, updated.Id);
    }

    [Fact]
    public void DocumentId_New_GeneratesUniqueIds()
    {
        DocumentId a = DocumentId.New();
        DocumentId b = DocumentId.New();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DocumentId_Parse_RoundTrips()
    {
        DocumentId id = DocumentId.New();
        DocumentId parsed = DocumentId.Parse(id.ToString());
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void PhysicalSize_IsContinuous_WhenHeightIsZero()
    {
        PhysicalSize continuous = PhysicalSize.FromMillimetres(62.0, 0);
        Assert.True(continuous.IsContinuous);

        PhysicalSize dieCut = PhysicalSize.FromMillimetres(17.0, 54.0);
        Assert.False(dieCut.IsContinuous);
    }

    [Fact]
    public void MicrometreRect_Intersects_Works()
    {
        MicrometreRect a = new(new(0), new(0), new(1000), new(1000));
        MicrometreRect b = new(new(500), new(500), new(1000), new(1000));
        MicrometreRect c = new(new(2000), new(2000), new(1000), new(1000));

        Assert.True(a.Intersects(b));
        Assert.False(a.Intersects(c));
    }

    [Fact]
    public void LineElement_BoundsIncludeThickness()
    {
        LineElement line = new(
            "l1",
            new(new(0), new(0)),
            new(new(1000), new(0)),
            new(200),
            InkChannel.Black);

        Assert.Equal(-100, line.Bounds.X.Value);
        Assert.Equal(-100, line.Bounds.Y.Value);
        Assert.Equal(1200, line.Bounds.Width.Value);
        Assert.Equal(200, line.Bounds.Height.Value);
    }
}
