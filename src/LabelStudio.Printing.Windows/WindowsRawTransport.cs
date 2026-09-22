using System.ComponentModel;
using System.Runtime.InteropServices;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Windows;

public sealed class WindowsRawTransport : IRawPrinterTransport
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidParameter = 87;
    private const uint FailedJobStatuses =
        0x00000001 | // Paused
        0x00000002 | // Error
        0x00000020 | // Offline
        0x00000040 | // Paper out
        0x00000200 | // Blocked device queue
        0x00000400;  // User intervention

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
                $"OpenPrinter failed for '{target.QueueName}': {new Win32Exception(error).Message} (Win32 error {error}).");
        }

        try
        {
            uint queuedJobs = GetQueuedJobCount(hPrinter);
            if (queuedJobs > 0)
            {
                return new RawSubmissionResult(false, null, 0,
                    $"Printer queue '{target.QueueName}' already contains {queuedJobs} job(s). " +
                    "Resolve or cancel the existing job before printing again; do not retry while its physical outcome is uncertain.");
            }

            NativeMethods.DocInfo1 docInfo = new()
            {
                pDocName = "LabelStudio Print Job",
                pOutputFile = null!,
                pDatatype = "RAW",
            };

            uint jobId = NativeMethods.StartDocPrinter(hPrinter, 1, ref docInfo);
            if (jobId == 0)
            {
                int error = Marshal.GetLastWin32Error();
                return new RawSubmissionResult(false, null, 0,
                    $"StartDocPrinter failed for '{target.QueueName}': {new Win32Exception(error).Message} (Win32 error {error}).");
            }

            bool documentOpen = true;
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
                                $"WritePrinter failed: {new Win32Exception(error).Message} (Win32 error {error}).");
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

                if (!NativeMethods.EndDocPrinter(hPrinter))
                {
                    int error = Marshal.GetLastWin32Error();
                    return new RawSubmissionResult(false, jobId > int.MaxValue ? null : (int)jobId, totalWritten,
                        $"EndDocPrinter failed: {new Win32Exception(error).Message} (Win32 error {error}).");
                }

                documentOpen = false;
                int? spoolerJobId = jobId > int.MaxValue ? null : (int)jobId;
                string? jobError = MonitorForImmediateFailure(
                    hPrinter,
                    target.QueueName,
                    jobId,
                    cancellationToken);

                return jobError is null
                    ? new RawSubmissionResult(true, spoolerJobId, totalWritten, null)
                    : new RawSubmissionResult(false, spoolerJobId, totalWritten, jobError);
            }
            finally
            {
                if (documentOpen)
                {
                    NativeMethods.AbortPrinter(hPrinter);
                }
            }
        }
        finally
        {
            hPrinter.Dispose();
        }
    }

    private static uint GetQueuedJobCount(SafePrinterHandle printer)
    {
        NativeMethods.GetPrinter(printer, 2, IntPtr.Zero, 0, out uint required);
        int error = Marshal.GetLastWin32Error();
        if (required == 0 || error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to inspect the printer queue before submission.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!NativeMethods.GetPrinter(printer, 2, buffer, required, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to read the printer queue before submission.");
            }

            return Marshal.PtrToStructure<NativeMethods.PrinterInfo2>(buffer).Jobs;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? MonitorForImmediateFailure(
        SafePrinterHandle printer,
        string queueName,
        uint jobId,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetJobStatus(printer, jobId, out uint status, out string? statusText))
            {
                return null;
            }

            if ((status & FailedJobStatuses) != 0)
            {
                string statuses = FormatJobStatus(status);
                string deviceMessage = statusText ?? "The printer did not provide additional details.";

                return $"Printer queue '{queueName}' rejected spooler job {jobId} ({statuses}). " +
                    $"{deviceMessage} Check that the installed label roll matches the document, the cover is closed, " +
                    "and Editor Lite mode is off. Resolve or cancel this job before retrying.";
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static bool TryGetJobStatus(
        SafePrinterHandle printer,
        uint jobId,
        out uint status,
        out string? statusText)
    {
        status = 0;
        statusText = null;
        NativeMethods.GetJob(printer, jobId, 1, IntPtr.Zero, 0, out uint required);
        int error = Marshal.GetLastWin32Error();
        if (required == 0)
        {
            if (error == ErrorInvalidParameter)
            {
                return false;
            }

            throw new Win32Exception(error, $"Unable to inspect spooler job {jobId}.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!NativeMethods.GetJob(printer, jobId, 1, buffer, required, out _))
            {
                error = Marshal.GetLastWin32Error();
                if (error == ErrorInvalidParameter)
                {
                    return false;
                }

                throw new Win32Exception(error, $"Unable to read spooler job {jobId}.");
            }

            NativeMethods.JobInfo1 job = Marshal.PtrToStructure<NativeMethods.JobInfo1>(buffer);
            status = job.Status;
            statusText = job.pStatus == IntPtr.Zero ? null : Marshal.PtrToStringUni(job.pStatus);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string FormatJobStatus(uint status)
    {
        List<string> values = [];
        AddStatus(values, status, 0x00000001, "paused");
        AddStatus(values, status, 0x00000002, "error");
        AddStatus(values, status, 0x00000008, "spooling");
        AddStatus(values, status, 0x00000010, "printing");
        AddStatus(values, status, 0x00000020, "offline");
        AddStatus(values, status, 0x00000040, "paper out");
        AddStatus(values, status, 0x00000200, "blocked");
        AddStatus(values, status, 0x00000400, "user intervention required");
        AddStatus(values, status, 0x00002000, "retained");
        return values.Count == 0 ? $"status 0x{status:X8}" : string.Join(", ", values);
    }

    private static void AddStatus(List<string> values, uint status, uint flag, string description)
    {
        if ((status & flag) != 0)
        {
            values.Add(description);
        }
    }
}
