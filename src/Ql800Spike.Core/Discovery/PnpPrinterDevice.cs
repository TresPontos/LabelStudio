namespace Ql800Spike.Core.Discovery;

public sealed record PnpPrinterDevice(
    string InstanceId,
    string? FriendlyName,
    string? Manufacturer,
    string? DeviceDescription,
    string? Location,
    IReadOnlyList<string> HardwareIds);

public enum CorrelationConfidence
{
    None,
    Low,
    Medium,
    High,
    Ambiguous,
}

public sealed record PrinterDeviceCorrelation(
    string QueueName,
    string? DeviceInstanceId,
    CorrelationConfidence Confidence,
    IReadOnlyList<string> Evidence);

public sealed record PrinterDiscoveryReport(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<PrinterQueueInfo> Queues,
    IReadOnlyList<PnpPrinterDevice> PnpDevices,
    IReadOnlyList<PrinterDeviceCorrelation> Correlations);
