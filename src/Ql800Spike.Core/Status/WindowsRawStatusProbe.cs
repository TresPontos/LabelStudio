using System.ComponentModel;
using System.Runtime.InteropServices;
using Ql800Spike.Core.Discovery;
using Ql800Spike.Core.RasterProtocol;

namespace Ql800Spike.Core.Status;

public sealed record RawStatusSubmission(
    DateTimeOffset SubmittedAtUtc,
    string PrinterName,
    uint SpoolerJobId,
    int RequestedBytes,
    uint WrittenBytes,
    string RequestHex);

public sealed record RawStatusReadResult(
    RawStatusSubmission Submission,
    DateTimeOffset ReadCompletedAtUtc,
    bool ReadSucceeded,
    int Win32Error,
    uint BytesRead,
    string ResponseHex);

public sealed class WindowsRawStatusProbe
{
    public RawStatusReadResult Probe(
        string printerName,
        Action<RawStatusSubmission>? onSubmitted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (!NativeMethods.OpenPrinter(printerName, out SafePrinterHandle printer, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open printer '{printerName}'.");
        }

        using (printer)
        {
            NativeMethods.DocInfo1 document = new()
            {
                DocumentName = "QL-800 Spike - Status Request Only",
                OutputFile = null,
                DataType = "RAW",
            };

            uint jobId = NativeMethods.StartDocPrinter(printer, 1, ref document);
            if (jobId == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the RAW status-request job.");
            }

            bool documentOpen = true;
            try
            {
                byte[] request = QlRasterJobEncoder.StatusRequest.ToArray();
                if (!NativeMethods.WritePrinter(
                        printer,
                        request,
                        checked((uint)request.Length),
                        out uint written))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to write the Brother status request.");
                }

                if (written != request.Length)
                {
                    throw new IOException(
                        $"The spooler accepted {written} of {request.Length} status-request bytes.");
                }

                if (!NativeMethods.EndDocPrinter(printer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to end the RAW status-request job.");
                }

                documentOpen = false;
                RawStatusSubmission submission = new(
                    DateTimeOffset.UtcNow,
                    printerName,
                    jobId,
                    request.Length,
                    written,
                    Convert.ToHexString(request));
                onSubmitted?.Invoke(submission);

                byte[] response = new byte[4_096];
                bool readSucceeded = NativeMethods.ReadPrinter(
                    printer,
                    response,
                    checked((uint)response.Length),
                    out uint bytesRead);
                int readError = readSucceeded ? 0 : Marshal.GetLastWin32Error();

                return new RawStatusReadResult(
                    submission,
                    DateTimeOffset.UtcNow,
                    readSucceeded,
                    readError,
                    bytesRead,
                    bytesRead == 0 ? string.Empty : Convert.ToHexString(response.AsSpan(0, checked((int)bytesRead))));
            }
            finally
            {
                if (documentOpen)
                {
                    NativeMethods.AbortPrinter(printer);
                }
            }
        }
    }
}
