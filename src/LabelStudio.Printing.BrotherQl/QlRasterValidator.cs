using System.Buffers.Binary;
using LabelStudio.Printing;

namespace LabelStudio.Printing.BrotherQl;

public sealed record QlValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    int RasterLineCount,
    byte PrintInformationFlags,
    byte ExpandedMode,
    bool AutoCut,
    byte CutEvery,
    int FeedMarginDots);

public sealed class QlRasterValidator
{
    public QlValidationResult Validate(ReadOnlySpan<byte> payload, MediaProfile media, BrotherQlMediaMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(mapping);

        List<string> errors = [];
        int offset = 0;

        for (int i = 0; i < QlRasterEncoder.InvalidationByteCount; i++)
        {
            if (!TryRead(payload, ref offset, out byte value))
            {
                errors.Add("Payload ended during invalidation prefix.");
                return CreateFailure(errors);
            }
            if (value != 0) errors.Add($"Invalidation byte {i} is 0x{value:X2}, expected 0x00.");
        }

        Expect(payload, ref offset, errors, "initialize", 0x1B, 0x40);
        Expect(payload, ref offset, errors, "raster mode", 0x1B, 0x69, 0x61, 0x01);
        Expect(payload, ref offset, errors, "status notification", 0x1B, 0x69, 0x21, 0x00);
        Expect(payload, ref offset, errors, "print information prefix", 0x1B, 0x69, 0x7A);

        byte flags = Read(payload, ref offset, errors, "flags");
        byte expectedFlags = media.Kind == MediaKind.DieCut ? (byte)0x8E : (byte)0x86;
        Compare(flags, expectedFlags, errors, "flags");
        Compare(Read(payload, ref offset, errors, "media type"), mapping.PrintInformationMediaType, errors, "media type");
        Compare(Read(payload, ref offset, errors, "width"), (byte)mapping.ProtocolWidthMillimetres, errors, "width");
        Compare(Read(payload, ref offset, errors, "length"), (byte)mapping.ProtocolLengthMillimetres, errors, "length");

        uint rasterCount = 0;
        if (offset + 4 <= payload.Length)
        {
            rasterCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
        }
        else errors.Add("Payload ended in raster count field.");

        Compare(Read(payload, ref offset, errors, "page flag"), 0x00, errors, "page flag");
        Compare(Read(payload, ref offset, errors, "terminator"), 0x00, errors, "terminator");

        Expect(payload, ref offset, errors, "various mode prefix", 0x1B, 0x69, 0x4D);
        byte variousMode = Read(payload, ref offset, errors, "various mode");
        bool autoCut = (variousMode & 0x40) != 0;
        if ((variousMode & ~0x40) != 0)
            errors.Add($"Various mode has unsupported bits: 0x{variousMode:X2}.");

        Expect(payload, ref offset, errors, "cut-every prefix", 0x1B, 0x69, 0x41);
        byte cutEvery = Read(payload, ref offset, errors, "cut-every");
        if (cutEvery == 0) errors.Add("Cut-every cannot be zero.");

        Expect(payload, ref offset, errors, "expanded mode prefix", 0x1B, 0x69, 0x4B);
        byte expandedMode = Read(payload, ref offset, errors, "expanded mode");
        if ((expandedMode & ~(0x01 | 0x08 | 0x40)) != 0)
            errors.Add($"Expanded mode has unsupported bits: 0x{expandedMode:X2}.");

        bool twoColour = (expandedMode & 0x01) != 0;
        if (mapping.RequiresTwoColourRaster && !twoColour)
            errors.Add($"Media '{media.ProfileId}' requires two-colour raster.");

        Expect(payload, ref offset, errors, "feed margin prefix", 0x1B, 0x69, 0x64);
        byte marginLow = Read(payload, ref offset, errors, "margin low");
        byte marginHigh = Read(payload, ref offset, errors, "margin high");
        int feedMargin = marginLow | (marginHigh << 8);

        if (media.Kind == MediaKind.DieCut && feedMargin != 0)
            errors.Add("Die-cut media must use zero feed margin.");
        else if (media.Kind == MediaKind.Continuous &&
                 (feedMargin < mapping.MinimumFeedMarginDots || feedMargin > mapping.MaximumFeedMarginDots))
            errors.Add("Continuous feed margin is outside range.");

        Expect(payload, ref offset, errors, "compression off", 0x4D, 0x00);

        for (uint row = 0; row < rasterCount; row++)
        {
            if (twoColour)
            {
                Expect(payload, ref offset, errors, $"black row {row} header", 0x77, 0x01, 0x5A);
                if (!AdvanceRaster(payload, ref offset, mapping, row, "black", errors)) break;
                Expect(payload, ref offset, errors, $"red row {row} header", 0x77, 0x02, 0x5A);
                if (!AdvanceRaster(payload, ref offset, mapping, row, "red", errors)) break;
            }
            else
            {
                Expect(payload, ref offset, errors, $"row {row} header", 0x67, 0x00, 0x5A);
                if (!AdvanceRaster(payload, ref offset, mapping, row, "black", errors)) break;
            }
        }

        Compare(Read(payload, ref offset, errors, "final print"), 0x1A, errors, "final print");
        if (offset != payload.Length)
            errors.Add($"Payload has {payload.Length - offset} trailing bytes.");

        if (media.Kind == MediaKind.DieCut && rasterCount != mapping.PrintableLengthDots)
            errors.Add($"Die-cut raster count {rasterCount} != expected {mapping.PrintableLengthDots}.");

        return new QlValidationResult(errors.Count == 0, errors, checked((int)rasterCount),
            flags, expandedMode, autoCut, cutEvery, feedMargin);
    }

    private static void ValidatePrintableHeadArea(ReadOnlySpan<byte> row, BrotherQlMediaMapping mapping, uint rowIndex, ICollection<string> errors)
    {
        int protocolStart = QlRasterEncoder.HeadWidthDots - (mapping.HeadLeftBlankDots + mapping.PrintableWidthDots);
        int protocolEnd = QlRasterEncoder.HeadWidthDots - mapping.HeadLeftBlankDots;

        for (int x = 0; x < QlRasterEncoder.HeadWidthDots; x++)
        {
            if (x >= protocolStart && x < protocolEnd) continue;
            int byteIndex = x / 8;
            int bit = 7 - (x % 8);
            if ((row[byteIndex] & (1 << bit)) != 0)
            {
                errors.Add($"Raster row {rowIndex} sets non-printable protocol pin {x}.");
                return;
            }
        }
    }

    private static bool AdvanceRaster(ReadOnlySpan<byte> payload, ref int offset, BrotherQlMediaMapping mapping, uint row, string plane, ICollection<string> errors)
    {
        if (offset + QlRasterEncoder.RasterBytesPerLine > payload.Length)
        {
            errors.Add($"Payload ended in {plane} raster row {row}.");
            offset = payload.Length;
            return false;
        }
        ValidatePrintableHeadArea(payload.Slice(offset, QlRasterEncoder.RasterBytesPerLine), mapping, row, errors);
        offset += QlRasterEncoder.RasterBytesPerLine;
        return true;
    }

    private static void Expect(ReadOnlySpan<byte> payload, ref int offset, ICollection<string> errors, string field, params ReadOnlySpan<byte> expected)
    {
        foreach (byte expectedByte in expected)
        {
            byte actual = Read(payload, ref offset, errors, field);
            if (actual != expectedByte)
                errors.Add($"Invalid {field}: expected 0x{expectedByte:X2}, got 0x{actual:X2}.");
        }
    }

    private static byte Read(ReadOnlySpan<byte> payload, ref int offset, ICollection<string> errors, string field)
    {
        if (!TryRead(payload, ref offset, out byte value))
        {
            errors.Add($"Payload ended reading {field}.");
            return 0;
        }
        return value;
    }

    private static bool TryRead(ReadOnlySpan<byte> payload, ref int offset, out byte value)
    {
        if (offset >= payload.Length) { value = 0; return false; }
        value = payload[offset++];
        return true;
    }

    private static void Compare(byte actual, byte expected, ICollection<string> errors, string field)
    {
        if (actual != expected)
            errors.Add($"Invalid {field}: expected 0x{expected:X2}, got 0x{actual:X2}.");
    }

    private static QlValidationResult CreateFailure(IReadOnlyList<string> errors) =>
        new(false, errors, 0, 0, 0, false, 0, 0);
}