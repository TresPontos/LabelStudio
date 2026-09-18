using Ql800Spike.Core.Status;

namespace Ql800Spike.Tests;

public sealed class QlStatusDecoderTests
{
    [Fact]
    public void DecodesIdleQl800WithDk22251Geometry()
    {
        byte[] packet = CreatePacket();
        packet[10] = 62;
        packet[11] = 0x4A;
        packet[17] = 0;

        QlPrinterStatus status = new QlStatusDecoder().Decode(packet);

        Assert.Equal("QL-800", status.Model);
        Assert.Equal(0x4A, status.Media.MediaType);
        Assert.Equal(62, status.Media.WidthMillimetres);
        Assert.Equal(0, status.Media.LengthMillimetres);
        Assert.Equal(QlStatusType.ReplyToStatusRequest, status.StatusType);
        Assert.Equal(QlPhaseType.Receiving, status.PhaseType);
        Assert.Empty(status.Errors);
    }

    [Fact]
    public void DecodesDk11204AndActiveErrors()
    {
        byte[] packet = CreatePacket();
        packet[8] = 0x01;
        packet[9] = 0x11;
        packet[10] = 17;
        packet[11] = 0x4B;
        packet[17] = 54;
        packet[18] = 0x02;

        QlPrinterStatus status = new QlStatusDecoder().Decode(packet);

        Assert.Equal(0x4B, status.Media.MediaType);
        Assert.Equal(17, status.Media.WidthMillimetres);
        Assert.Equal(54, status.Media.LengthMillimetres);
        Assert.Equal(QlStatusType.ErrorOccurred, status.StatusType);
        Assert.Collection(
            status.Errors,
            error => Assert.Equal("no-media", error.Code),
            error => Assert.Equal("replace-media", error.Code),
            error => Assert.Equal("cover-open", error.Code));
    }

    [Fact]
    public void PreservesUnknownErrorBits()
    {
        byte[] packet = CreatePacket();
        packet[8] = 0xC8;
        packet[9] = 0x28;

        QlPrinterStatus status = new QlStatusDecoder().Decode(packet);

        Assert.Equal(0xC8, status.UnknownErrorBits1);
        Assert.Equal(0x28, status.UnknownErrorBits2);
        Assert.Empty(status.Errors);
    }

    [Fact]
    public void RejectsInvalidPacketFraming()
    {
        byte[] packet = CreatePacket();
        packet[0] = 0;

        Assert.Throws<InvalidDataException>(() => new QlStatusDecoder().Decode(packet));
    }

    private static byte[] CreatePacket()
    {
        byte[] packet = new byte[32];
        packet[0] = 0x80;
        packet[1] = 0x20;
        packet[2] = 0x42;
        packet[3] = 0x34;
        packet[4] = 0x38;
        packet[5] = 0x30;
        packet[6] = 0x30;
        packet[14] = 0x3F;
        return packet;
    }
}
