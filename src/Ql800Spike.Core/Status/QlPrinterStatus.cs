using Ql800Spike.Core.Media;

namespace Ql800Spike.Core.Status;

public enum QlStatusType : byte
{
    ReplyToStatusRequest = 0x00,
    PrintingCompleted = 0x01,
    ErrorOccurred = 0x02,
    TurnedOff = 0x04,
    Notification = 0x05,
    PhaseChange = 0x06,
}

public enum QlPhaseType : byte
{
    Receiving = 0x00,
    Printing = 0x01,
}

public enum QlNotification : byte
{
    None = 0x00,
    CoolingStarted = 0x03,
    CoolingFinished = 0x04,
}

public sealed record QlStatusError(string Code, string Description);

public sealed record QlPrinterStatus(
    string Model,
    byte ModelCode,
    MediaStatusSignature Media,
    byte VariousMode,
    QlStatusType? StatusType,
    byte RawStatusType,
    QlPhaseType? PhaseType,
    byte RawPhaseType,
    ushort PhaseNumber,
    QlNotification? Notification,
    byte RawNotification,
    IReadOnlyList<QlStatusError> Errors,
    byte UnknownErrorBits1,
    byte UnknownErrorBits2,
    byte[] RawBytes);
