using System.Runtime.InteropServices;

namespace Ql800Spike.Core.Discovery;

internal static class NativeMethods
{
    internal const uint PrinterEnumLocal = 0x00000002;
    internal const uint PrinterEnumConnections = 0x00000004;
    internal const int ErrorInsufficientBuffer = 122;

    internal const uint DigcfPresent = 0x00000002;
    internal const uint SpdrpDeviceDescription = 0x00000000;
    internal const uint SpdrpHardwareId = 0x00000001;
    internal const uint SpdrpManufacturer = 0x0000000B;
    internal const uint SpdrpFriendlyName = 0x0000000C;
    internal const uint SpdrpLocationInformation = 0x0000000D;

    internal const short DcFields = 1;
    internal const short DcMinExtent = 4;
    internal const short DcMaxExtent = 5;
    internal const short DcDuplex = 7;
    internal const short DcEnumResolutions = 13;
    internal const short DcPaperNames = 16;
    internal const short DcCopies = 18;
    internal const short DcManufacturer = 23;
    internal const short DcModel = 24;
    internal const short DcMediaReady = 29;
    internal const short DcColorDevice = 32;

    internal static readonly Guid PrinterClassGuid =
        new("4D36E979-E325-11CE-BFC1-08002BE10318");

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint bufferSize,
        out uint bytesNeeded,
        out uint returned);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenPrinter(
        string printerName,
        out SafePrinterHandle printer,
        IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterDriverW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPrinterDriver(
        SafePrinterHandle printer,
        string? environment,
        uint level,
        IntPtr driverInfo,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint StartDocPrinter(
        SafePrinterHandle printer,
        uint level,
        ref DocInfo1 documentInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndDocPrinter(SafePrinterHandle printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AbortPrinter(SafePrinterHandle printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WritePrinter(
        SafePrinterHandle printer,
        byte[] buffer,
        uint bufferSize,
        out uint bytesWritten);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadPrinter(
        SafePrinterHandle printer,
        byte[] buffer,
        uint bufferSize,
        out uint bytesRead);

    [DllImport("winspool.drv", EntryPoint = "GetJobW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetJob(
        SafePrinterHandle printer,
        uint jobId,
        uint level,
        IntPtr jobInfo,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int DeviceCapabilities(
        string device,
        string port,
        short capability,
        IntPtr output,
        IntPtr devMode);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        SafeDeviceInfoSetHandle deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        char[]? deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrinterInfo2
    {
        internal IntPtr ServerName;
        internal IntPtr PrinterName;
        internal IntPtr ShareName;
        internal IntPtr PortName;
        internal IntPtr DriverName;
        internal IntPtr Comment;
        internal IntPtr Location;
        internal IntPtr DevMode;
        internal IntPtr SepFile;
        internal IntPtr PrintProcessor;
        internal IntPtr DataType;
        internal IntPtr Parameters;
        internal IntPtr SecurityDescriptor;
        internal uint Attributes;
        internal uint Priority;
        internal uint DefaultPriority;
        internal uint StartTime;
        internal uint UntilTime;
        internal uint Status;
        internal uint Jobs;
        internal uint AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DriverInfo2
    {
        internal uint Version;
        internal IntPtr Name;
        internal IntPtr Environment;
        internal IntPtr DriverPath;
        internal IntPtr DataFile;
        internal IntPtr ConfigurationFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string DocumentName;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string DataType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JobInfo1
    {
        internal uint JobId;
        internal IntPtr PrinterName;
        internal IntPtr MachineName;
        internal IntPtr UserName;
        internal IntPtr Document;
        internal IntPtr DataType;
        internal IntPtr StatusText;
        internal uint Status;
        internal uint Priority;
        internal uint Position;
        internal uint TotalPages;
        internal uint PagesPrinted;
        internal SystemTime Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDevInfoData
    {
        internal uint Size;
        internal Guid ClassGuid;
        internal uint DeviceInstance;
        internal IntPtr Reserved;
    }
}
