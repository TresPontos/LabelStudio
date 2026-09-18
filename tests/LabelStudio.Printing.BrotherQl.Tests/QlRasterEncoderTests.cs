using LabelStudio.Printing;
using LabelStudio.Printing.BrotherQl;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.BrotherQl.Tests;

public class QlRasterEncoderTests
{
    private static MediaProfile Dk22251 => MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");
    private static MediaProfile Dk11204 => MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");

    [Fact]
    public void TransformPhysicalRowToProtocol_MirrorsFull720DotRow()
    {
        byte[] physicalRow = new byte[90];
        SetBit(physicalRow, 0);
        SetBit(physicalRow, 719);

        byte[] protocolRow = QlRasterEncoder.TransformPhysicalRowToProtocol(physicalRow);

        Assert.True(GetBit(protocolRow, 719), "Physical dot 0 must map to protocol dot 719.");
        Assert.True(GetBit(protocolRow, 0), "Physical dot 719 must map to protocol dot 0.");
    }

    [Fact]
    public void TransformPhysicalRowToProtocol_CenterDotIsInvariant()
    {
        byte[] physicalRow = new byte[90];
        SetBit(physicalRow, 359);
        SetBit(physicalRow, 360);

        byte[] protocolRow = QlRasterEncoder.TransformPhysicalRowToProtocol(physicalRow);

        Assert.True(GetBit(protocolRow, 360), "Physical dot 359 maps to protocol dot 360.");
        Assert.True(GetBit(protocolRow, 359), "Physical dot 360 maps to protocol dot 359.");
    }

    [Fact]
    public void TransformPhysicalRowToProtocol_DieCutPlacement_DK11204()
    {
        byte[] physicalRow = new byte[90];
        for (int x = 555; x < 720; x++)
        {
            SetBit(physicalRow, x);
        }

        byte[] protocolRow = QlRasterEncoder.TransformPhysicalRowToProtocol(physicalRow);

        int protocolStart = 720 - 555 - 165;
        int protocolEnd = 720 - 555;

        for (int x = protocolStart; x < protocolEnd; x++)
        {
            Assert.True(GetBit(protocolRow, x),
                $"Protocol dot {x} should be set for DK-11204 printable area.");
        }

        for (int x = 0; x < protocolStart; x++)
        {
            Assert.False(GetBit(protocolRow, x),
                $"Protocol dot {x} should be clear (left of DK-11204 printable area).");
        }
    }

    [Fact]
    public void TransformPhysicalRowToProtocol_DK22251_PrintableArea()
    {
        byte[] physicalRow = new byte[90];
        for (int x = 12; x < 12 + 696; x++)
        {
            SetBit(physicalRow, x);
        }

        byte[] protocolRow = QlRasterEncoder.TransformPhysicalRowToProtocol(physicalRow);

        int protocolStart = 720 - 12 - 696;
        int protocolEnd = 720 - 12;

        for (int x = protocolStart; x < protocolEnd; x++)
        {
            Assert.True(GetBit(protocolRow, x), $"Protocol dot {x} should be set for DK-22251.");
        }
    }

    [Fact]
    public void Encode_DK22251_ProducesValidPayload()
    {
        MonochromeRaster black = new(720, 354);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        QlRasterJobOptions options = new(Dk22251, mapping, black, null, 35);
        QlEncodedJob job = new QlRasterEncoder().Encode(options);

        QlValidationResult validation = new QlRasterValidator().Validate(job.Payload, Dk22251, mapping);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.True(job.TwoColour);
        Assert.Equal(354, job.RasterLineCount);
        Assert.Equal(35, job.FeedMarginDots);
    }

    [Fact]
    public void Encode_DK11204_ProducesValidPayload()
    {
        MonochromeRaster black = new(720, 566);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk11204();
        QlRasterJobOptions options = new(Dk11204, mapping, black, null, 0);
        QlEncodedJob job = new QlRasterEncoder().Encode(options);

        QlValidationResult validation = new QlRasterValidator().Validate(job.Payload, Dk11204, mapping);

        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.False(job.TwoColour);
        Assert.Equal(566, job.RasterLineCount);
        Assert.Equal(0, job.FeedMarginDots);
    }

    [Fact]
    public void Encode_DK22251_WithRedPlane_ProducesTwoColour()
    {
        MonochromeRaster black = new(720, 354);
        MonochromeRaster red = new(720, 354);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        QlRasterJobOptions options = new(Dk22251, mapping, black, red, 35);
        QlEncodedJob job = new QlRasterEncoder().Encode(options);

        Assert.True(job.TwoColour);
        Assert.True(job.Payload.Length > 0);
    }

    [Fact]
    public void Encode_DieCut_NonZeroFeedMargin_Throws()
    {
        MonochromeRaster black = new(720, 566);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk11204();
        QlRasterJobOptions options = new(Dk11204, mapping, black, null, 35);

        Assert.Throws<ArgumentException>(() => new QlRasterEncoder().Encode(options));
    }

    [Fact]
    public void Encode_DieCut_WrongRowCount_Throws()
    {
        MonochromeRaster black = new(720, 500);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk11204();
        QlRasterJobOptions options = new(Dk11204, mapping, black, null, 0);

        Assert.Throws<ArgumentException>(() => new QlRasterEncoder().Encode(options));
    }

    [Fact]
    public void Encode_WrongRasterWidth_Throws()
    {
        MonochromeRaster black = new(600, 354);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        QlRasterJobOptions options = new(Dk22251, mapping, black, null, 35);

        Assert.Throws<ArgumentException>(() => new QlRasterEncoder().Encode(options));
    }

    [Fact]
    public void Encode_Has400ByteInvalidationPrefix()
    {
        MonochromeRaster black = new(720, 354);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        QlRasterJobOptions options = new(Dk22251, mapping, black, null, 35);
        QlEncodedJob job = new QlRasterEncoder().Encode(options);

        for (int i = 0; i < 400; i++)
        {
            Assert.Equal(0, job.Payload[i]);
        }
    }

    [Fact]
    public void Encode_CompressionIsOff()
    {
        MonochromeRaster black = new(720, 354);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        QlRasterJobOptions options = new(Dk22251, mapping, black, null, 35);
        QlEncodedJob job = new QlRasterEncoder().Encode(options);
        ReadOnlySpan<byte> payload = job.Payload;

        bool found = false;
        for (int i = 400; i < payload.Length - 1; i++)
        {
            if (payload[i] == 0x4D && payload[i + 1] == 0x00)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Compression-off command (4D 00) not found in payload.");
    }

    private static void SetBit(byte[] row, int x) =>
        row[x / 8] |= (byte)(0x80 >> (x % 8));

    private static bool GetBit(byte[] row, int x) =>
        (row[x / 8] & (0x80 >> (x % 8))) != 0;
}