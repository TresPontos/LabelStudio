using System.Buffers.Binary;
using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;

namespace Ql800Spike.Core.RasterProtocol;

public sealed class QlRasterJobEncoder
{
    public const int InvalidationByteCount = 400;
    public const int HeadWidthDots = 720;
    public const int RasterBytesPerLine = 90;
    public const int RasterRecordBytes = 93;
    public const int TwoColourRasterRecordBytes = 186;

    public static ReadOnlySpan<byte> StatusRequest => [0x1B, 0x69, 0x53];

    public QlRasterJob Encode(QlRasterJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        using MemoryStream stream = new();
        bool twoColour = options.Media.BrotherQl.RequiresTwoColourRaster || options.RedRaster is not null;
        stream.Write(new byte[InvalidationByteCount]);
        Write(stream, 0x1B, 0x40);                         // Initialize.
        Write(stream, 0x1B, 0x69, 0x61, 0x01);             // Raster command mode.
        Write(stream, 0x1B, 0x69, 0x21, 0x00);             // Automatic status notification on.
        WritePrintInformation(stream, options);
        Write(stream, 0x1B, 0x69, 0x4D, options.AutoCut ? (byte)0x40 : (byte)0x00);
        Write(stream, 0x1B, 0x69, 0x41, options.CutEvery);

        byte expandedMode = 0;
        if (options.CutAtEnd)
        {
            expandedMode |= 0x08;
        }

        if (options.HighResolution)
        {
            expandedMode |= 0x40;
        }

        if (twoColour)
        {
            expandedMode |= 0x01;
        }

        Write(stream, 0x1B, 0x69, 0x4B, expandedMode);
        Write(stream, 0x1B, 0x69, 0x64,
            (byte)(options.FeedMarginDots & 0xFF),
            (byte)((options.FeedMarginDots >> 8) & 0xFF));
        Write(stream, 0x4D, 0x00);                         // Compression off.

        byte[] blankRedRow = new byte[RasterBytesPerLine];
        for (int row = 0; row < options.BlackRaster.Height; row++)
        {
            if (twoColour)
            {
                Write(stream, 0x77, 0x01, RasterBytesPerLine); // High-energy black plane.
                stream.Write(TransformPhysicalRowToProtocol(options.BlackRaster.GetRow(row), options.Media));
                Write(stream, 0x77, 0x02, RasterBytesPerLine); // Low-energy red plane.
                if (options.RedRaster is not null)
                {
                    stream.Write(TransformPhysicalRowToProtocol(options.RedRaster.GetRow(row), options.Media));
                }
                else
                {
                    stream.Write(blankRedRow);
                }
            }
            else
            {
                Write(stream, 0x67, 0x00, RasterBytesPerLine);
                stream.Write(TransformPhysicalRowToProtocol(options.BlackRaster.GetRow(row), options.Media));
            }
        }

        stream.WriteByte(0x1A);                            // Final print with feed.

        return new QlRasterJob(
            stream.ToArray(),
            options.Media.ProfileId,
            options.BlackRaster.Height,
            RasterBytesPerLine,
            twoColour ? TwoColourRasterRecordBytes : RasterRecordBytes,
            options.FeedMarginDots,
            twoColour,
            options.AutoCut,
            options.CutEvery,
            options.CutAtEnd,
            options.HighResolution);
    }

    private static void WritePrintInformation(Stream stream, QlRasterJobOptions options)
    {
        BrotherQlMediaMapping mapping = options.Media.BrotherQl;
        byte validFlags = options.Media.Kind == MediaKind.DieCut ? (byte)0x8E : (byte)0x86;
        Span<byte> rasterCount = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(rasterCount, checked((uint)options.BlackRaster.Height));

        Write(stream,
            0x1B,
            0x69,
            0x7A,
            validFlags,
            mapping.PrintInformationMediaType,
            mapping.ProtocolWidthMillimetres,
            mapping.ProtocolLengthMillimetres,
            rasterCount[0],
            rasterCount[1],
            rasterCount[2],
            rasterCount[3],
            0x00,
            0x00);
    }

    private static void ValidateOptions(QlRasterJobOptions options)
    {
        if (options.BlackRaster.Width != HeadWidthDots || options.BlackRaster.BytesPerRow != RasterBytesPerLine)
        {
            throw new ArgumentException("QL-800 raster data must contain the full 720-dot, 90-byte head width.");
        }

        if (options.CutEvery == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CutEvery must be between 1 and 255.");
        }

        BrotherQlMediaMapping mapping = options.Media.BrotherQl;
        if (options.Media.Kind == MediaKind.DieCut)
        {
            if (options.FeedMarginDots != 0)
            {
                throw new ArgumentException("Die-cut media requires a zero feed-margin command.");
            }

            if (options.BlackRaster.Height != mapping.PrintableLengthDots)
            {
                throw new ArgumentException(
                    $"Media '{options.Media.ProfileId}' requires exactly {mapping.PrintableLengthDots} raster rows.");
            }
        }
        else
        {
            if (options.FeedMarginDots < mapping.MinimumFeedMarginDots ||
                options.FeedMarginDots > mapping.MaximumFeedMarginDots)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"Continuous feed margin must be {mapping.MinimumFeedMarginDots}..{mapping.MaximumFeedMarginDots} dots.");
            }

            if (options.BlackRaster.Height is < 150 or > 11_811)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "QL-800 continuous raster length must be 150..11811 rows at 300 dpi.");
            }
        }

        if (options.RedRaster is not null &&
            (options.RedRaster.Width != options.BlackRaster.Width ||
             options.RedRaster.Height != options.BlackRaster.Height))
        {
            throw new ArgumentException("Black and red raster planes must have identical dimensions.");
        }

        EnsureNonPrintableHeadPinsAreBlank(options.BlackRaster, options.Media);
        if (options.RedRaster is not null)
        {
            EnsureNonPrintableHeadPinsAreBlank(options.RedRaster, options.Media);
        }
    }

    private static void EnsureNonPrintableHeadPinsAreBlank(
        MonochromeRaster raster,
        MediaProfile media)
    {
        BrotherQlMediaMapping mapping = media.BrotherQl;
        int printableEnd = mapping.HeadLeftBlankDots + mapping.PrintableWidthDots;

        for (int y = 0; y < raster.Height; y++)
        {
            for (int x = 0; x < mapping.HeadLeftBlankDots; x++)
            {
                if (raster.GetPixel(x, y))
                {
                    throw new ArgumentException($"Raster has ink in the left non-printable head area at ({x}, {y}).");
                }
            }

            for (int x = printableEnd; x < HeadWidthDots; x++)
            {
                if (raster.GetPixel(x, y))
                {
                    throw new ArgumentException($"Raster has ink in the right non-printable head area at ({x}, {y}).");
                }
            }
        }
    }

    private static byte[] TransformPhysicalRowToProtocol(
        ReadOnlySpan<byte> physicalRow,
        MediaProfile media)
    {
        byte[] protocolRow = new byte[RasterBytesPerLine];
        int start = media.BrotherQl.HeadLeftBlankDots;
        int width = media.BrotherQl.PrintableWidthDots;

        for (int x = 0; x < HeadWidthDots; x++)
        {
            if (!GetBit(physicalRow, x))
            {
                continue;
            }

            int destinationX = HeadWidthDots - 1 - x;
            SetBit(protocolRow, destinationX);
        }

        return protocolRow;
    }

    private static bool GetBit(ReadOnlySpan<byte> row, int x) =>
        (row[x / 8] & (0x80 >> (x % 8))) != 0;

    private static void SetBit(Span<byte> row, int x) =>
        row[x / 8] |= (byte)(0x80 >> (x % 8));

    private static void Write(Stream stream, params ReadOnlySpan<byte> bytes) => stream.Write(bytes);
}
