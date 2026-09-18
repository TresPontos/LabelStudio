using Ql800Spike.Core.Media;

namespace Ql800Spike.Tests;

public sealed class MediaCatalogTests
{
    private readonly MediaCatalog catalog = MediaCatalog.LoadBuiltIn();

    [Fact]
    public void Dk22251ContainsOfficialQl800Geometry()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");

        Assert.Equal(MediaKind.Continuous, media.Kind);
        Assert.Equal(62_000, media.PhysicalWidthMicrometres);
        Assert.Null(media.PhysicalLengthMicrometres);
        Assert.Equal([ThermalInk.Black, ThermalInk.Red], media.SupportedInks);
        Assert.Equal(259, media.BrotherQl.ReferenceMediaId);
        Assert.Equal(0x0A, media.BrotherQl.PrintInformationMediaType);
        Assert.Equal(0x4A, media.BrotherQl.StatusMediaType);
        Assert.Equal(12, media.BrotherQl.HeadLeftBlankDots);
        Assert.Equal(696, media.BrotherQl.PrintableWidthDots);
        Assert.Equal(12, media.BrotherQl.HeadRightBlankDots);
        Assert.True(media.BrotherQl.RequiresTwoColourRaster);
    }

    [Fact]
    public void Dk11204ContainsOfficialQl800Geometry()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-11204");

        Assert.Equal(MediaKind.DieCut, media.Kind);
        Assert.Equal(17_000, media.PhysicalWidthMicrometres);
        Assert.Equal(53_900, media.PhysicalLengthMicrometres);
        Assert.Equal(566, media.BrotherQl.PrintableLengthDots);
        Assert.Equal(269, media.BrotherQl.ReferenceMediaId);
        Assert.Equal(555, media.BrotherQl.HeadLeftBlankDots);
        Assert.Equal(165, media.BrotherQl.PrintableWidthDots);
        Assert.Equal(0, media.BrotherQl.HeadRightBlankDots);
        Assert.Equal(35, media.BrotherQl.FeedOffsetDots);
        Assert.False(media.BrotherQl.RequiresTwoColourRaster);
    }

    [Theory]
    [InlineData(0x4A, 62, 0, "brother.dk-22251")]
    [InlineData(0x4B, 17, 54, "brother.dk-11204")]
    public void StatusGeometryFindsCompatibleProfile(
        byte type,
        byte width,
        byte length,
        string expectedProfile)
    {
        MediaIdentification result = catalog.Identify(new MediaStatusSignature(type, width, length));

        MediaProfile match = Assert.Single(result.CompatibleProfiles);
        Assert.Equal(expectedProfile, match.ProfileId);
        Assert.False(result.IsExactSku);
        Assert.Contains("not the DK SKU", result.Explanation, StringComparison.Ordinal);
    }
}
