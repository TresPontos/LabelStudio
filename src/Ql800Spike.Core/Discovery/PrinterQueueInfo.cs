namespace Ql800Spike.Core.Discovery;

[Flags]
public enum WindowsPrinterStatus : uint
{
    None = 0,
    Paused = 0x00000001,
    Error = 0x00000002,
    PendingDeletion = 0x00000004,
    PaperJam = 0x00000008,
    PaperOut = 0x00000010,
    ManualFeed = 0x00000020,
    PaperProblem = 0x00000040,
    Offline = 0x00000080,
    IoActive = 0x00000100,
    Busy = 0x00000200,
    Printing = 0x00000400,
    OutputBinFull = 0x00000800,
    NotAvailable = 0x00001000,
    Waiting = 0x00002000,
    Processing = 0x00004000,
    Initializing = 0x00008000,
    WarmingUp = 0x00010000,
    TonerLow = 0x00020000,
    NoToner = 0x00040000,
    PagePunt = 0x00080000,
    UserIntervention = 0x00100000,
    OutOfMemory = 0x00200000,
    DoorOpen = 0x00400000,
    ServerUnknown = 0x00800000,
    PowerSave = 0x01000000,
}

public sealed record PrinterQueueInfo(
    string Name,
    string? ShareName,
    string PortName,
    string DriverName,
    string PrintProcessor,
    string DataType,
    string? Location,
    string? Comment,
    WindowsPrinterStatus Status,
    uint Attributes,
    uint Jobs,
    PrinterDriverInfo? Driver);

public sealed record PrinterDriverInfo(
    uint Version,
    string Name,
    string Environment,
    string DriverPath,
    string DataFile,
    string ConfigurationFile,
    string? FileVersion);

public sealed record PrinterCapabilities(
    string QueueName,
    string PortName,
    int DriverFields,
    int MinimumExtentRaw,
    int MaximumExtentRaw,
    bool SupportsDuplex,
    bool IsColorDevice,
    int MaximumCopies,
    IReadOnlyList<PrinterResolution> Resolutions,
    IReadOnlyList<string> DriverReportedReadyMedia,
    IReadOnlyList<string> PaperNames,
    string? Manufacturer,
    string? Model);

public sealed record PrinterResolution(int DpiX, int DpiY);
