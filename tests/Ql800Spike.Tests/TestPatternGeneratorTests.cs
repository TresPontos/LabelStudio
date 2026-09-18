using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;

namespace Ql800Spike.Tests;

public sealed class TestPatternGeneratorTests
{
    private readonly MediaCatalog catalog = MediaCatalog.LoadBuiltIn();
    private readonly TestPatternGenerator generator = new();

    [Theory]
    [InlineData("continuous-30", 284)]
    [InlineData("geometry-60", 639)]
    [InlineData("continuous-100", 1_111)]
    public void ContinuousPatternsPlanRasterRowsSeparatelyFromFeedMargin(
        string testId,
        int expectedRows)
    {
        GeneratedTestPattern pattern = generator.Generate(
            testId,
            catalog.GetRequired("brother.dk-22251"));

        Assert.Equal(720, pattern.Raster.Width);
        Assert.Equal(expectedRows, pattern.Raster.Height);
        Assert.Equal(35, pattern.FeedMarginDots);
    }

    [Fact]
    public void DieCutPatternUsesFixedPrintableRows()
    {
        GeneratedTestPattern pattern = generator.Generate(
            "diecut-placement",
            catalog.GetRequired("brother.dk-11204"));

        Assert.Equal(566, pattern.Raster.Height);
        Assert.Equal(0, pattern.FeedMarginDots);
    }

    [Fact]
    public void EveryDeclaredPatternGeneratesWithinItsMediaBounds()
    {
        foreach (string testId in generator.TestIds)
        {
            string profileId = testId == "diecut-placement"
                ? "brother.dk-11204"
                : "brother.dk-22251";
            GeneratedTestPattern pattern = generator.Generate(testId, catalog.GetRequired(profileId));

            Assert.Equal(720, pattern.Raster.Width);
            Assert.True(pattern.Raster.Height > 0);
        }
    }

    [Fact]
    public void ColourPlaneTestGeneratesSeparateBlackAndRedArtwork()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");

        GeneratedColourTestPattern pattern = generator.GenerateColourPlaneTest(media);

        Assert.Equal(720, pattern.BlackRaster.Width);
        Assert.Equal(284, pattern.BlackRaster.Height);
        Assert.Equal(pattern.BlackRaster.Width, pattern.RedRaster.Width);
        Assert.Equal(pattern.BlackRaster.Height, pattern.RedRaster.Height);
        Assert.NotEqual(pattern.BlackRaster.Data.ToArray(), pattern.RedRaster.Data.ToArray());
        Assert.Contains(pattern.BlackRaster.Data.Span.ToArray(), value => value != 0);
        Assert.Contains(pattern.RedRaster.Data.Span.ToArray(), value => value != 0);
    }
}
