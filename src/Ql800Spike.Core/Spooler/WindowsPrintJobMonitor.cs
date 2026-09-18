using System.ComponentModel;
using System.Runtime.InteropServices;
using Ql800Spike.Core.Discovery;

namespace Ql800Spike.Core.Spooler;

[Flags]
public enum WindowsJobStatus : uint
{
    None = 0,
    Paused = 0x00000001,
    Error = 0x00000002,
    Deleting = 0x00000004,
    Spooling = 0x00000008,
    Printing = 0x00000010,
    Offline = 0x00000020,
    PaperOut = 0x00000040,
    Printed = 0x00000080,
    Deleted = 0x00000100,
    BlockedDeviceQueue = 0x00000200,
    UserIntervention = 0x00000400,
    Restart = 0x00000800,
    Complete = 0x00001000,
    Retained = 0x00002000,
    RenderingLocally = 0x00004000,
}

public sealed record PrintJobSnapshot(
    DateTimeOffset ObservedAtUtc,
    uint JobId,
    string? DocumentName,
    string? DataType,
    string? StatusText,
    WindowsJobStatus Status,
    uint TotalPages,
    uint PagesPrinted);

public sealed record PrintJobTimeline(
    uint JobId,
    IReadOnlyList<PrintJobSnapshot> Snapshots,
    bool DisappearedFromQueue,
    bool TimedOut,
    DateTimeOffset CompletedAtUtc);

public sealed class WindowsPrintJobMonitor
{
    public PrintJobTimeline Monitor(
        string printerName,
        uint jobId,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        if (!NativeMethods.OpenPrinter(printerName, out SafePrinterHandle printer, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open printer '{printerName}'.");
        }

        using (printer)
        {
            List<PrintJobSnapshot> snapshots = [];
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (!TryGetSnapshot(printer, jobId, out PrintJobSnapshot? snapshot))
                {
                    return new PrintJobTimeline(jobId, snapshots, true, false, DateTimeOffset.UtcNow);
                }

                if (snapshot is not null &&
                    (snapshots.Count == 0 || snapshots[^1].Status != snapshot.Status || snapshots[^1].PagesPrinted != snapshot.PagesPrinted))
                {
                    snapshots.Add(snapshot);
                }

                Thread.Sleep(pollInterval);
            }

            return new PrintJobTimeline(jobId, snapshots, false, true, DateTimeOffset.UtcNow);
        }
    }

    private static bool TryGetSnapshot(
        SafePrinterHandle printer,
        uint jobId,
        out PrintJobSnapshot? snapshot)
    {
        snapshot = null;
        NativeMethods.GetJob(printer, jobId, 1, IntPtr.Zero, 0, out uint required);
        int error = Marshal.GetLastWin32Error();
        if (required == 0)
        {
            const int invalidParameter = 87;
            if (error == invalidParameter)
            {
                return false;
            }

            throw new Win32Exception(error, $"Unable to query spooler job {jobId}.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!NativeMethods.GetJob(printer, jobId, 1, buffer, required, out _))
            {
                error = Marshal.GetLastWin32Error();
                const int invalidParameter = 87;
                if (error == invalidParameter)
                {
                    return false;
                }

                throw new Win32Exception(error, $"Unable to read spooler job {jobId}.");
            }

            NativeMethods.JobInfo1 info = Marshal.PtrToStructure<NativeMethods.JobInfo1>(buffer);
            snapshot = new PrintJobSnapshot(
                DateTimeOffset.UtcNow,
                info.JobId,
                OptionalString(info.Document),
                OptionalString(info.DataType),
                OptionalString(info.StatusText),
                (WindowsJobStatus)info.Status,
                info.TotalPages,
                info.PagesPrinted);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? OptionalString(IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUni(value);
}
