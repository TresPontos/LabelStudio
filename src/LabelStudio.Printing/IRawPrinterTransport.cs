namespace LabelStudio.Printing;

public interface IRawPrinterTransport
{
    RawSubmissionResult Submit(
        PrinterTransportTarget target,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

public sealed record PrinterTransportTarget(
    string QueueName,
    string? PortName = null);

public sealed record RawSubmissionResult(
    bool Success,
    int? SpoolerJobId,
    int BytesWritten,
    string? Error);