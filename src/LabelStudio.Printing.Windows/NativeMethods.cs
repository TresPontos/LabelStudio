using System.Runtime.InteropServices;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Windows;

internal static class NativeMethods
{
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool OpenPrinter(string pPrinterName, out SafePrinterHandle hPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartDocPrinter(SafePrinterHandle hPrinter, int level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool AbortPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(SafePrinterHandle hPrinter, IntPtr pBuf, int cbBuf, out int cbWritten);

    [StructLayout(LayoutKind.Sequential)]
    public struct DocInfo1
    {
        public string pDocName;
        public string pOutputFile;
        public string pDatatype;
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