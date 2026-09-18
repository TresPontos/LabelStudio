using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;
using Ql800Spike.Core.RasterProtocol;

namespace Ql800Spike.Tests;

public sealed class QlRasterJobEncoderTests
{
    private readonly MediaCatalog catalog = MediaCatalog.LoadBuiltIn();
    private readonly TestPatternGenerator patterns = new();
    private readonly QlRasterJobEncoder encoder = new();

    [Fact]
    public void EncodesDocumentedContinuousJobPrefixAndLength()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        GeneratedTestPattern pattern = patterns.Generate("continuous-30", media);

        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));

        Assert.All(job.Payload[..400], value => Assert.Equal(0, value));
        Assert.Equal([0x1B, 0x40], job.Payload[400..402]);
        Assert.Equal([0x1B, 0x69, 0x61, 0x01], job.Payload[402..406]);
        Assert.Equal([0x1B, 0x69, 0x21, 0x00], job.Payload[406..410]);
        Assert.Equal(
            [0x1B, 0x69, 0x7A, 0x86, 0x0A, 0x3E, 0x00, 0x1C, 0x01, 0x00, 0x00, 0x00, 0x00],
            job.Payload[410..423]);
        Assert.Equal([0x1B, 0x69, 0x4D, 0x40], job.Payload[423..427]);
        Assert.Equal([0x1B, 0x69, 0x41, 0x01], job.Payload[427..431]);
        Assert.Equal([0x1B, 0x69, 0x4B, 0x09], job.Payload[431..435]);
        Assert.Equal([0x1B, 0x69, 0x64, 0x23, 0x00], job.Payload[435..440]);
        Assert.Equal([0x4D, 0x00], job.Payload[440..442]);
        Assert.Equal([0x77, 0x01, 0x5A], job.Payload[442..445]);
        Assert.Equal([0x77, 0x02, 0x5A], job.Payload[535..538]);
        Assert.All(job.Payload[538..628], value => Assert.Equal(0, value));
        Assert.Equal(443 + (pattern.Raster.Height * 186), job.Payload.Length);
        Assert.True(job.TwoColour);
        Assert.Equal(0x1A, job.Payload[^1]);
    }

    [Fact]
    public void EncodesDocumentedDieCutMediaAndRasterCount()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-11204");
        GeneratedTestPattern pattern = patterns.Generate("diecut-placement", media);

        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));

        Assert.Equal(
            [0x1B, 0x69, 0x7A, 0x8E, 0x0B, 0x11, 0x36, 0x36, 0x02, 0x00, 0x00, 0x00, 0x00],
            job.Payload[410..423]);
        Assert.Equal([0x1B, 0x69, 0x64, 0x00, 0x00], job.Payload[435..440]);
    }

    [Fact]
    public void BlankRowsAreFullUncompressedRasterRecords()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        MonochromeRaster raster = new(720, 150);

        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(media, raster, 35));

        Assert.Equal([0x77, 0x01, 0x5A], job.Payload[442..445]);
        Assert.All(job.Payload[445..535], value => Assert.Equal(0, value));
        Assert.Equal([0x77, 0x02, 0x5A], job.Payload[535..538]);
        Assert.All(job.Payload[538..628], value => Assert.Equal(0, value));
        Assert.Equal([0x77, 0x01, 0x5A], job.Payload[628..631]);
    }

    [Fact]
    public void RejectsInkOutsideMediaHeadMapping()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-11204");
        MonochromeRaster raster = new(720, 566);
        raster.SetPixel(0, 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            encoder.Encode(new QlRasterJobOptions(media, raster, 0)));

        Assert.Contains("non-printable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusRequestIsSeparateFromPrintPayload()
    {
        Assert.Equal([0x1B, 0x69, 0x53], QlRasterJobEncoder.StatusRequest.ToArray());

        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        GeneratedTestPattern pattern = patterns.Generate("canary-30", media);
        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(media, pattern.Raster, pattern.FeedMarginDots));

        Assert.DoesNotContain((byte)0x53, job.Payload[400..410]);
    }

    [Fact]
    public void MirrorsPhysicalArtworkWithinTheMediaPrintArea()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        MonochromeRaster raster = new(720, 150);
        raster.SetPixel(media.BrotherQl.HeadLeftBlankDots, 0);

        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(media, raster, 35));

        ReadOnlySpan<byte> firstBlackRow = job.Payload.AsSpan(445, 90);
        int expectedProtocolX = media.BrotherQl.HeadLeftBlankDots +
            media.BrotherQl.PrintableWidthDots - 1;
        Assert.Equal(
            0x80 >> (expectedProtocolX % 8),
            firstBlackRow[expectedProtocolX / 8]);
        Assert.Equal(1, firstBlackRow.ToArray().Sum(value => System.Numerics.BitOperations.PopCount(value)));
    }

    [Fact]
    public void EncodesSeparateBlackAndRedPlanesInDocumentedOrder()
    {
        MediaProfile media = catalog.GetRequired("brother.dk-22251");
        GeneratedColourTestPattern pattern = patterns.GenerateColourPlaneTest(media);

        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(
            media,
            pattern.BlackRaster,
            pattern.FeedMarginDots,
            pattern.RedRaster));
        QlRasterValidationResult validation = new QlRasterJobValidator().Validate(job.Payload, media);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.Equal([0x77, 0x01, 0x5A], job.Payload[442..445]);
        Assert.Equal([0x77, 0x02, 0x5A], job.Payload[535..538]);
        Assert.True(job.TwoColour);
        Assert.Equal(0x09, validation.ExpandedMode);
        int row25 = 442 + (25 * QlRasterJobEncoder.TwoColourRasterRecordBytes);
        Assert.Contains(job.Payload[(row25 + 3)..(row25 + 93)], value => value != 0);
        Assert.DoesNotContain(job.Payload[(row25 + 96)..(row25 + 186)], value => value != 0);
    }
}
