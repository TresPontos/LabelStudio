using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;
using Ql800Spike.Core.RasterProtocol;
using Ql800Spike.Core.Spooler;

namespace Ql800Spike.Tests;

public sealed class RawCanarySafetyTests
{
    [Fact]
    public void PhysicalCanaryHasTheExpectedBoundedConfiguration()
    {
        MediaProfile media = MediaCatalog.LoadBuiltIn().GetRequired("brother.dk-22251");
        GeneratedTestPattern pattern = new TestPatternGenerator().Generate("canary-30", media);
        QlRasterJob job = new QlRasterJobEncoder().Encode(
            new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));
        QlRasterValidationResult validation = new QlRasterJobValidator().Validate(job.Payload, media);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.Equal(284, pattern.Raster.Height);
        Assert.Equal(35, pattern.FeedMarginDots);
        Assert.Equal(30_000, pattern.RequestedFinishedLengthMicrometres);
        Assert.Equal(53_267, job.Payload.Length);
        Assert.True(job.AutoCut);
        Assert.True(job.CutAtEnd);
        Assert.Equal(1, job.CutEvery);
        Assert.False(job.HighResolution);
        Assert.True(job.TwoColour);
        Assert.Equal(186, job.EncodedBytesPerRasterLine);
        Assert.Equal(0x1A, job.Payload[^1]);
    }

    [Fact]
    public void RawTransportRejectsAnEmptyPayloadBeforeOpeningPrinter()
    {
        WindowsRawSpoolTransport transport = new();

        Assert.Throws<ArgumentException>(() =>
            transport.Submit("unused", "unused", Array.Empty<byte>()));
    }

    [Fact]
    public void DieCutPlacementTestUsesFixedMonochromeGeometry()
    {
        MediaProfile media = MediaCatalog.LoadBuiltIn().GetRequired("brother.dk-11204");
        GeneratedTestPattern pattern = new TestPatternGenerator().Generate("diecut-placement", media);
        QlRasterJob job = new QlRasterJobEncoder().Encode(
            new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));
        QlRasterValidationResult validation = new QlRasterJobValidator().Validate(job.Payload, media);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.Equal(566, pattern.Raster.Height);
        Assert.Equal(0, pattern.FeedMarginDots);
        Assert.Equal(53_081, job.Payload.Length);
        Assert.False(job.TwoColour);
        Assert.Equal(0x08, validation.ExpandedMode);
        Assert.Equal(0x8E, validation.PrintInformationFlags);
    }
}
