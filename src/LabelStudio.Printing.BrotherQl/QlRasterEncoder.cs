using System.Buffers.Binary;
using LabelStudio.Printing;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.BrotherQl;

public sealed class QlRasterEncoder
{
    public const int InvalidationByteCount = 400;
    public const int HeadWidthDots = 720;
    public const int RasterBytesPerLine = 90;
    public const int RasterRecordBytes = 93;
    public const int TwoColourRasterRecordBytes = 186;

    public static ReadOnlySpan<byte> StatusRequest => [0x1B, 0x69, 0x53];

    public QlEncodedJob Encode(QlRasterJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        using MemoryStream stream = new();
        bool twoColour = options.Mapping.RequiresTwoColourRaster || options.RedRaster is not null;
        stream.Write(new byte[InvalidationByteCount]);
        Write(stream, 0x1B, 0x40);
        Write(stream, 0x1B, 0x69, 0x61, 0x01);
        Write(stream, 0x1B, 0x69, 0x21, 0x00);
        WritePrintInformation(stream, options);
        Write(stream, 0x1B, 0x69, 0x4D, options.AutoCut ? (byte)0x40 : (byte)0x00);
        Write(stream, 0x1B, 0x69, 0x41, options.CutEvery);

        byte expandedMode = 0;
        if (options.CutAtEnd) expandedMode |= 0x08;
        if (options.HighResolution) expandedMode |= 0x40;
        if (twoColour) expandedMode |= 0x01;

        Write(stream, 0x1B, 0x69, 0x4B, expandedMode);
        Write(stream, 0x1B, 0x69, 0x64,
            (byte)(options.FeedMarginDots & 0xFF),
            (byte)((options.FeedMarginDots >> 8) & 0xFF));
        Write(stream, 0x4D, 0x00);

        byte[] blankRedRow = new byte[RasterBytesPerLine];
        for (int row = 0; row < options.BlackRaster.Height; row++)
        {
            if (twoColour)
            {
                Write(stream, 0x77, 0x01, RasterBytesPerLine);
                stream.Write(TransformPhysicalRowToProtocol(options.BlackRaster.GetRow(row)));
                Write(stream, 0x77, 0x02, RasterBytesPerLine);
                if (options.RedRaster is not null)
                {
                    stream.Write(TransformPhysicalRowToProtocol(options.RedRaster.GetRow(row)));
                }
                else
                {
                    stream.Write(blankRedRow);
                }
            }
            else
            {
                Write(stream, 0x67, 0x00, RasterBytesPerLine);
                stream.Write(TransformPhysicalRowToProtocol(options.BlackRaster.GetRow(row)));
            }
        }

        stream.WriteByte(0x1A);

        return new QlEncodedJob(
            stream.ToArray(),
            options.Media.ProfileId,
            options.BlackRaster.Height,
            twoColour,
            options.FeedMarginDots,
            options.AutoCut,
            options.CutEvery,
            options.CutAtEnd);
    }

    private static void WritePrintInformation(Stream stream, QlRasterJobOptions options)
    {
        BrotherQlMediaMapping mapping = options.Mapping;
        byte validFlags = options.Media.Kind == MediaKind.DieCut ? (byte)0x8E : (byte)0x86;
        Span<byte> rasterCount = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(rasterCount, checked((uint)options.BlackRaster.Height));

        Write(stream,
            0x1B, 0x69, 0x7A,
            validFlags,
            mapping.PrintInformationMediaType,
            (byte)mapping.ProtocolWidthMillimetres,
            (byte)mapping.ProtocolLengthMillimetres,
            rasterCount[0], rasterCount[1], rasterCount[2], rasterCount[3],
            0x00, 0x00);
    }

    private static void ValidateOptions(QlRasterJobOptions options)
    {
        if (options.BlackRaster.Width != HeadWidthDots)
        {
            throw new ArgumentException(
                $"Raster width must be {HeadWidthDots} dots, got {options.BlackRaster.Width}.");
        }

        if (options.CutEvery == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CutEvery must be 1..255.");
        }

        if (options.Media.Kind == MediaKind.DieCut)
        {
            if (options.FeedMarginDots != 0)
            {
                throw new ArgumentException("Die-cut media requires zero feed margin.");
            }

            if (options.BlackRaster.Height != options.Mapping.PrintableLengthDots)
            {
                throw new ArgumentException(
                    $"Die-cut media requires {options.Mapping.PrintableLengthDots} raster rows, got {options.BlackRaster.Height}.");
            }
        }
        else
        {
            if (options.FeedMarginDots < options.Mapping.MinimumFeedMarginDots ||
                options.FeedMarginDots > options.Mapping.MaximumFeedMarginDots)
            {
                throw new ArgumentOutOfRangeException(nameof(options),
                    $"Continuous feed margin must be {options.Mapping.MinimumFeedMarginDots}..{options.Mapping.MaximumFeedMarginDots} dots.");
            }

            if (options.BlackRaster.Height is < 150 or > 11_811)
            {
                throw new ArgumentOutOfRangeException(nameof(options),
                    "Continuous raster length must be 150..11811 rows at 300 dpi.");
            }
        }

        if (options.RedRaster is not null &&
            (options.RedRaster.Width != options.BlackRaster.Width ||
             options.RedRaster.Height != options.BlackRaster.Height))
        {
            throw new ArgumentException("Black and red raster planes must have identical dimensions.");
        }
    }

    /// <summary>
    /// Mirrors the entire 720-dot physical head row to protocol coordinates.
    /// Physical dot x maps to protocol dot (719 - x).
    /// This full-row mirror is the validated Brother QL protocol transform.
    /// NEVER mirror only within the printable area — that causes incorrect
    /// head positioning for offset media like DK-11204.
    /// </summary>
    public static byte[] TransformPhysicalRowToProtocol(ReadOnlySpan<byte> physicalRow)
    {
        byte[] protocolRow = new byte[RasterBytesPerLine];

        for (int x = 0; x < HeadWidthDots; x++)
        {
            if (!GetBit(physicalRow, x)) continue;
            SetBit(protocolRow, HeadWidthDots - 1 - x);
        }

        return protocolRow;
    }

    private static bool GetBit(ReadOnlySpan<byte> row, int x) =>
        (row[x / 8] & (0x80 >> (x % 8))) != 0;

    private static void SetBit(Span<byte> row, int x) =>
        row[x / 8] |= (byte)(0x80 >> (x % 8));

    private static void Write(Stream stream, params ReadOnlySpan<byte> bytes) => stream.Write(bytes);
}