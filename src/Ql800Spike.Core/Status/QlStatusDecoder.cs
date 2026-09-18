using System.Buffers.Binary;
using Ql800Spike.Core.Media;

namespace Ql800Spike.Core.Status;

public sealed class QlStatusDecoder
{
    public const int PacketLength = 32;

    public QlPrinterStatus Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != PacketLength)
        {
            throw new InvalidDataException($"Brother status packet must be exactly {PacketLength} bytes.");
        }

        ValidateFixedFields(packet);

        byte error1 = packet[8];
        byte error2 = packet[9];
        List<QlStatusError> errors = DecodeErrors(error1, error2);
        ushort phaseNumber = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(20, 2));

        return new QlPrinterStatus(
            DecodeModel(packet[4]),
            packet[4],
            new MediaStatusSignature(packet[11], packet[10], packet[17]),
            packet[15],
            Enum.IsDefined((QlStatusType)packet[18]) ? (QlStatusType)packet[18] : null,
            packet[18],
            Enum.IsDefined((QlPhaseType)packet[19]) ? (QlPhaseType)packet[19] : null,
            packet[19],
            phaseNumber,
            Enum.IsDefined((QlNotification)packet[22]) ? (QlNotification)packet[22] : null,
            packet[22],
            errors,
            (byte)(error1 & 0xC8),
            (byte)(error2 & 0x28),
            packet.ToArray());
    }

    private static void ValidateFixedFields(ReadOnlySpan<byte> packet)
    {
        (int Offset, byte Expected)[] fields =
        [
            (0, 0x80),
            (1, 0x20),
            (2, 0x42),
            (3, 0x34),
            (5, 0x30),
            (6, 0x30),
            (7, 0x00),
            (12, 0x00),
            (13, 0x00),
            (14, 0x3F),
            (16, 0x00),
            (23, 0x00),
            (24, 0x00),
            (25, 0x00),
            (26, 0x00),
            (27, 0x00),
            (28, 0x00),
            (29, 0x00),
            (30, 0x00),
            (31, 0x00),
        ];

        foreach ((int offset, byte expected) in fields)
        {
            if (packet[offset] != expected)
            {
                throw new InvalidDataException(
                    $"Invalid Brother status field at offset {offset}: expected 0x{expected:X2}, found 0x{packet[offset]:X2}.");
            }
        }
    }

    private static string DecodeModel(byte modelCode) => modelCode switch
    {
        0x38 => "QL-800",
        0x39 => "QL-810W",
        0x41 => "QL-820NWB",
        _ => $"Unknown (0x{modelCode:X2})",
    };

    private static List<QlStatusError> DecodeErrors(byte error1, byte error2)
    {
        List<QlStatusError> errors = [];
        AddIfSet(errors, error1, 0x01, "no-media", "No media is installed.");
        AddIfSet(errors, error1, 0x02, "end-of-media", "The die-cut roll reached end of media.");
        AddIfSet(errors, error1, 0x04, "cutter-jam", "The cutter is jammed.");
        AddIfSet(errors, error1, 0x10, "printer-in-use", "The printer is in use.");
        AddIfSet(errors, error1, 0x20, "printer-turned-off", "The printer reported a turned-off state.");

        AddIfSet(errors, error2, 0x01, "replace-media", "Installed media does not match the job.");
        AddIfSet(errors, error2, 0x02, "expansion-buffer-full", "The expansion buffer is full.");
        AddIfSet(errors, error2, 0x04, "communication-error", "A communication error occurred.");
        AddIfSet(errors, error2, 0x10, "cover-open", "The printer cover is open.");
        AddIfSet(errors, error2, 0x40, "media-feed-error", "Media cannot be fed or media end was detected.");
        AddIfSet(errors, error2, 0x80, "system-error", "The printer reported a system error.");
        return errors;
    }

    private static void AddIfSet(
        ICollection<QlStatusError> errors,
        byte value,
        byte mask,
        string code,
        string description)
    {
        if ((value & mask) != 0)
        {
            errors.Add(new QlStatusError(code, description));
        }
    }
}
