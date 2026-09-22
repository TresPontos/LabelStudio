using System.Runtime.InteropServices;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Windows;

internal static class NativeMethods
{
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool OpenPrinter(string pPrinterName, out SafePrinterHandle hPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint StartDocPrinter(SafePrinterHandle hPrinter, int level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool AbortPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(SafePrinterHandle hPrinter, IntPtr pBuf, int cbBuf, out int cbWritten);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetPrinter(
        SafePrinterHandle hPrinter,
        uint level,
        IntPtr printerInfo,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("winspool.drv", EntryPoint = "GetJobW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetJob(
        SafePrinterHandle hPrinter,
        uint jobId,
        uint level,
        IntPtr jobInfo,
        uint bufferSize,
        out uint bytesNeeded);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDatatype;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrinterInfo2
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobInfo1
    {
        public uint JobId;
        public IntPtr pPrinterName;
        public IntPtr pMachineName;
        public IntPtr pUserName;
        public IntPtr pDocument;
        public IntPtr pDatatype;
        public IntPtr pStatus;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
        public SystemTime Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }
}

internal sealed class SafePrinterHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    private SafePrinterHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClosePrinter(handle);
        return true;
    }
}
