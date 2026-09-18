using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;
using Ql800Spike.Core.RasterProtocol;

namespace Ql800Spike.Tests;

public sealed class QlRasterJobValidatorTests
{
    private readonly MediaCatalog catalog = MediaCatalog.LoadBuiltIn();

    [Fact]
    public void AcceptsEncoderOutput()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        GeneratedTestPattern pattern = new TestPatternGenerator().Generate("geometry-60", media);
        QlRasterJob job = new QlRasterJobEncoder().Encode(
            new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));

        QlRasterValidationResult result = new QlRasterJobValidator().Validate(job.Payload, media);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(pattern.Raster.Height, result.RasterLineCount);
        Assert.True(result.AutoCut);
        Assert.Equal(1, result.CutEvery);
        Assert.Equal(35, result.FeedMarginDots);
    }

    [Fact]
    public void RejectsTamperedRasterCount()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        GeneratedTestPattern pattern = new TestPatternGenerator().Generate("canary-30", media);
        byte[] payload = new QlRasterJobEncoder().Encode(
            new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots)).Payload;
        payload[417]--;

        QlRasterValidationResult result = new QlRasterJobValidator().Validate(payload, media);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void EveryDeclaredPatternEncodesAndValidates()
    {
        TestPatternGenerator generator = new();
        QlRasterJobEncoder encoder = new();
        QlRasterJobValidator validator = new();

        foreach (string testId in generator.TestIds)
        {
            string profileId = testId == "diecut-placement"
                ? "brother.dk-11204"
                : "brother.dk-22251";
            MediaProfile media = catalog.GetRequired(profileId);
            GeneratedTestPattern pattern = generator.Generate(testId, media);
            QlRasterJob job = encoder.Encode(
                new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));

            QlRasterValidationResult result = validator.Validate(job.Payload, media);

            Assert.True(result.IsValid, $"{testId}: {string.Join(" | ", result.Errors)}");
        }
    }
}
