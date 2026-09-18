using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Tests;

public class MediaCatalogTests
{
    [Fact]
    public void BuiltIn_Contains_DK22251()
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile profile = catalog.Get("brother.dk-22251");

        Assert.Equal("DK-22251", profile.Sku);
        Assert.Equal(MediaKind.Continuous, profile.Kind);
        Assert.True(profile.SupportsRed);
        Assert.Equal(62_000, profile.PhysicalWidthMicrometres);
    }

    [Fact]
    public void BuiltIn_Contains_DK11204()
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile profile = catalog.Get("brother.dk-11204");

        Assert.Equal("DK-11204", profile.Sku);
        Assert.Equal(MediaKind.DieCut, profile.Kind);
        Assert.False(profile.SupportsRed);
        Assert.Equal(17_000, profile.PhysicalWidthMicrometres);
        Assert.Equal(53_900, profile.PhysicalLengthMicrometres);
    }

    [Fact]
    public void Get_Unknown_Throws()
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        Assert.Throws<KeyNotFoundException>(() => catalog.Get("unknown"));
    }

    [Fact]
    public void TryGet_ReturnsTrue_ForKnown()
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        Assert.True(catalog.TryGet("brother.dk-22251", out _));
        Assert.False(catalog.TryGet("unknown", out _));
    }
}