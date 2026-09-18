using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ql800Spike.Core.Discovery;

namespace Ql800Spike.Core.Spooler;

public sealed record RawSpoolSubmission(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string PrinterName,
    string DocumentName,
    uint SpoolerJobId,
    int RequestedBytes,
    long WrittenBytes,
    int WriteCalls,
    string PayloadSha256);

public sealed class WindowsRawSpoolTransport
{
    public RawSpoolSubmission Submit(
        string printerName,
        string documentName,
        ReadOnlySpan<byte> payload,
        Action<uint>? onJobCreated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        if (payload.IsEmpty)
        {
            throw new ArgumentException("RAW payload cannot be empty.", nameof(payload));
        }

        byte[] ownedPayload = payload.ToArray();
        DateTimeOffset started = DateTimeOffset.UtcNow;

        if (!NativeMethods.OpenPrinter(printerName, out SafePrinterHandle printer, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open printer '{printerName}'.");
        }

        using (printer)
        {
            NativeMethods.DocInfo1 document = new()
            {
                DocumentName = documentName,
                OutputFile = null,
                DataType = "RAW",
            };

            uint jobId = NativeMethods.StartDocPrinter(printer, 1, ref document);
            if (jobId == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the RAW print job.");
            }

            onJobCreated?.Invoke(jobId);
            bool documentOpen = true;
            long totalWritten = 0;
            int writeCalls = 0;
            try
            {
                while (totalWritten < ownedPayload.Length)
                {
                    byte[] remaining = ownedPayload.AsSpan(checked((int)totalWritten)).ToArray();
                    if (!NativeMethods.WritePrinter(
                            printer,
                            remaining,
                            checked((uint)remaining.Length),
                            out uint written))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to write the RAW print payload.");
                    }

                    writeCalls++;
                    if (written == 0)
                    {
                        throw new IOException("WritePrinter succeeded without accepting any bytes.");
                    }

                    totalWritten += written;
                }

                if (!NativeMethods.EndDocPrinter(printer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to end the RAW print job.");
                }

                documentOpen = false;
                return new RawSpoolSubmission(
                    started,
                    DateTimeOffset.UtcNow,
                    printerName,
                    documentName,
                    jobId,
                    ownedPayload.Length,
                    totalWritten,
                    writeCalls,
                    Convert.ToHexString(SHA256.HashData(ownedPayload)));
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
