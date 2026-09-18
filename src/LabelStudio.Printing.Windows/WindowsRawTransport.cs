using System.Runtime.InteropServices;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Windows;

public sealed class WindowsRawTransport : IRawPrinterTransport
{
    public RawSubmissionResult Submit(
        PrinterTransportTarget target,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (payload.Length == 0)
        {
            return new RawSubmissionResult(false, null, 0, "Empty payload.");
        }

        IntPtr pDefault = IntPtr.Zero;
        if (!NativeMethods.OpenPrinter(target.QueueName, out SafePrinterHandle hPrinter, pDefault))
        {
            int error = Marshal.GetLastWin32Error();
            return new RawSubmissionResult(false, null, 0,
                $"OpenPrinter failed: Win32 error {error}.");
        }

        try
        {
            NativeMethods.DocInfo1 docInfo = new()
            {
                pDocName = "LabelStudio Print Job",
                pOutputFile = null!,
                pDatatype = "RAW",
            };

            if (!NativeMethods.StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                int error = Marshal.GetLastWin32Error();
                return new RawSubmissionResult(false, null, 0,
                    $"StartDocPrinter failed: Win32 error {error}.");
            }

            try
            {
                int totalWritten = 0;
                int offset = 0;
                while (offset < payload.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int chunkSize = Math.Min(payload.Length - offset, 65536);
                    byte[] chunk = payload.Slice(offset, chunkSize).ToArray();

                    GCHandle pin = GCHandle.Alloc(chunk, GCHandleType.Pinned);
                    try
                    {
                        if (!NativeMethods.WritePrinter(hPrinter,
                            pin.AddrOfPinnedObject(), chunkSize, out int written))
                        {
                            int error = Marshal.GetLastWin32Error();
                            return new RawSubmissionResult(false, null, totalWritten,
                                $"WritePrinter failed: Win32 error {error}.");
                        }

                        if (written == 0)
                        {
                            return new RawSubmissionResult(false, null, totalWritten,
                                "WritePrinter accepted 0 bytes.");
                        }

                        totalWritten += written;
                        offset += written;
                    }
                    finally
                    {
                        pin.Free();
                    }
                }

                return new RawSubmissionResult(true, null, totalWritten, null);
            }
            finally
            {
                NativeMethods.EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            hPrinter.Dispose();
        }
    }
}