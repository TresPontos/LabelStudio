using System.Buffers.Binary;
using Ql800Spike.Core.Media;

namespace Ql800Spike.Core.RasterProtocol;

public sealed record QlRasterValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    int RasterLineCount,
    byte PrintInformationFlags,
    byte ExpandedMode,
    bool AutoCut,
    byte CutEvery,
    int FeedMarginDots);

public sealed class QlRasterJobValidator
{
    public QlRasterValidationResult Validate(ReadOnlySpan<byte> payload, MediaProfile media)
    {
        ArgumentNullException.ThrowIfNull(media);

        List<string> errors = [];
        int offset = 0;

        for (int index = 0; index < QlRasterJobEncoder.InvalidationByteCount; index++)
        {
            if (!TryRead(payload, ref offset, out byte value))
            {
                errors.Add("Payload ended during the 400-byte invalidation prefix.");
                return CreateFailure(errors);
            }

            if (value != 0)
            {
                errors.Add($"Invalidation byte {index} is 0x{value:X2}, expected 0x00.");
            }
        }

        Expect(payload, ref offset, errors, "initialize", 0x1B, 0x40);
        Expect(payload, ref offset, errors, "raster mode", 0x1B, 0x69, 0x61, 0x01);
        Expect(payload, ref offset, errors, "automatic status notification", 0x1B, 0x69, 0x21, 0x00);
        Expect(payload, ref offset, errors, "print information prefix", 0x1B, 0x69, 0x7A);

        byte flags = Read(payload, ref offset, errors, "print-information flags");
        byte expectedFlags = media.Kind == MediaKind.DieCut ? (byte)0x8E : (byte)0x86;
        Compare(flags, expectedFlags, errors, "print-information flags");
        Compare(Read(payload, ref offset, errors, "media type"), media.BrotherQl.PrintInformationMediaType, errors, "media type");
        Compare(Read(payload, ref offset, errors, "media width"), media.BrotherQl.ProtocolWidthMillimetres, errors, "media width");
        Compare(Read(payload, ref offset, errors, "media length"), media.BrotherQl.ProtocolLengthMillimetres, errors, "media length");

        uint rasterCount = 0;
        if (offset + 4 <= payload.Length)
        {
            rasterCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
        }
        else
        {
            errors.Add("Payload ended in the raster-count field.");
            offset = payload.Length;
        }

        Compare(Read(payload, ref offset, errors, "page flag"), 0x00, errors, "page flag");
        Compare(Read(payload, ref offset, errors, "print-information terminator"), 0x00, errors, "print-information terminator");

        Expect(payload, ref offset, errors, "various mode prefix", 0x1B, 0x69, 0x4D);
        byte variousMode = Read(payload, ref offset, errors, "various mode");
        bool autoCut = (variousMode & 0x40) != 0;
        if ((variousMode & ~0x40) != 0)
        {
            errors.Add($"Various mode contains unsupported bits: 0x{variousMode:X2}.");
        }

        Expect(payload, ref offset, errors, "cut-every prefix", 0x1B, 0x69, 0x41);
        byte cutEvery = Read(payload, ref offset, errors, "cut-every value");
        if (cutEvery == 0)
        {
            errors.Add("Cut-every value cannot be zero.");
        }

        Expect(payload, ref offset, errors, "expanded mode prefix", 0x1B, 0x69, 0x4B);
        byte expandedMode = Read(payload, ref offset, errors, "expanded mode");
        if ((expandedMode & ~(0x01 | 0x08 | 0x40)) != 0)
        {
            errors.Add($"Expanded mode contains unsupported monochrome bits: 0x{expandedMode:X2}.");
        }

        bool twoColour = (expandedMode & 0x01) != 0;
        if (media.BrotherQl.RequiresTwoColourRaster && !twoColour)
        {
            errors.Add($"Media '{media.ProfileId}' requires two-colour raster framing.");
        }

        Expect(payload, ref offset, errors, "feed margin prefix", 0x1B, 0x69, 0x64);
        byte marginLow = Read(payload, ref offset, errors, "feed margin low byte");
        byte marginHigh = Read(payload, ref offset, errors, "feed margin high byte");
        int feedMargin = marginLow | (marginHigh << 8);

        if (media.Kind == MediaKind.DieCut && feedMargin != 0)
        {
            errors.Add("Die-cut media must use a zero feed margin.");
        }
        else if (media.Kind == MediaKind.Continuous &&
                 (feedMargin < media.BrotherQl.MinimumFeedMarginDots ||
                  feedMargin > media.BrotherQl.MaximumFeedMarginDots))
        {
            errors.Add("Continuous feed margin is outside the media profile range.");
        }

        Expect(payload, ref offset, errors, "compression off", 0x4D, 0x00);

        for (uint row = 0; row < rasterCount; row++)
        {
            if (twoColour)
            {
                Expect(payload, ref offset, errors, $"black raster row {row} header", 0x77, 0x01, 0x5A);
                if (!ValidateAndAdvanceRasterPlane(payload, ref offset, media, row, "black", errors))
                {
                    break;
                }

                Expect(payload, ref offset, errors, $"red raster row {row} header", 0x77, 0x02, 0x5A);
                if (!ValidateAndAdvanceRasterPlane(payload, ref offset, media, row, "red", errors))
                {
                    break;
                }
            }
            else
            {
                Expect(payload, ref offset, errors, $"raster row {row} header", 0x67, 0x00, 0x5A);
                if (!ValidateAndAdvanceRasterPlane(payload, ref offset, media, row, "black", errors))
                {
                    break;
                }
            }
        }

        Compare(Read(payload, ref offset, errors, "final print command"), 0x1A, errors, "final print command");
        if (offset != payload.Length)
        {
            errors.Add($"Payload contains {payload.Length - offset} trailing bytes.");
        }

        if (media.Kind == MediaKind.DieCut && rasterCount != media.BrotherQl.PrintableLengthDots)
        {
            errors.Add(
                $"Die-cut raster count is {rasterCount}, expected {media.BrotherQl.PrintableLengthDots}.");
        }

        return new QlRasterValidationResult(
            errors.Count == 0,
            errors,
            checked((int)rasterCount),
            flags,
            expandedMode,
            autoCut,
            cutEvery,
            feedMargin);
    }

    private static void ValidatePrintableHeadArea(
        ReadOnlySpan<byte> row,
        MediaProfile media,
        uint rowIndex,
        ICollection<string> errors)
    {
        int protocolStart = QlRasterJobEncoder.HeadWidthDots - (media.BrotherQl.HeadLeftBlankDots + media.BrotherQl.PrintableWidthDots);
        int protocolEnd = QlRasterJobEncoder.HeadWidthDots - media.BrotherQl.HeadLeftBlankDots;

        for (int x = 0; x < QlRasterJobEncoder.HeadWidthDots; x++)
        {
            if (x >= protocolStart && x < protocolEnd)
            {
                continue;
            }

            int byteIndex = x / 8;
            int bit = 7 - (x % 8);
            if ((row[byteIndex] & (1 << bit)) != 0)
            {
                errors.Add($"Raster row {rowIndex} sets non-printable head pin {x}.");
                return;
            }
        }
    }

    private static bool ValidateAndAdvanceRasterPlane(
        ReadOnlySpan<byte> payload,
        ref int offset,
        MediaProfile media,
        uint row,
        string plane,
        ICollection<string> errors)
    {
        if (offset + QlRasterJobEncoder.RasterBytesPerLine > payload.Length)
        {
            errors.Add($"Payload ended in {plane} raster row {row}.");
            offset = payload.Length;
            return false;
        }

        ValidatePrintableHeadArea(
            payload.Slice(offset, QlRasterJobEncoder.RasterBytesPerLine),
            media,
            row,
            errors);
        offset += QlRasterJobEncoder.RasterBytesPerLine;
        return true;
    }

    private static void Expect(
        ReadOnlySpan<byte> payload,
        ref int offset,
        ICollection<string> errors,
        string field,
        params ReadOnlySpan<byte> expected)
    {
        foreach (byte expectedByte in expected)
        {
            byte actual = Read(payload, ref offset, errors, field);
            if (actual != expectedByte)
            {
                errors.Add($"Invalid {field}: expected 0x{expectedByte:X2}, found 0x{actual:X2}.");
            }
        }
    }

    private static byte Read(
        ReadOnlySpan<byte> payload,
        ref int offset,
        ICollection<string> errors,
        string field)
    {
        if (!TryRead(payload, ref offset, out byte value))
        {
            errors.Add($"Payload ended while reading {field}.");
        }

        return value;
    }

    private static bool TryRead(ReadOnlySpan<byte> payload, ref int offset, out byte value)
    {
        if (offset >= payload.Length)
        {
            value = 0;
            return false;
        }

        value = payload[offset++];
        return true;
    }

    private static void Compare(byte actual, byte expected, ICollection<string> errors, string field)
    {
        if (actual != expected)
        {
            errors.Add($"Invalid {field}: expected 0x{expected:X2}, found 0x{actual:X2}.");
        }
    }

    private static QlRasterValidationResult CreateFailure(IReadOnlyList<string> errors) =>
        new(false, errors, 0, 0, 0, false, 0, 0);
}
