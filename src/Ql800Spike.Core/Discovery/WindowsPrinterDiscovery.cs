using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ql800Spike.Core.Discovery;

public sealed class WindowsPrinterDiscovery
{
    public PrinterDiscoveryReport Discover()
    {
        IReadOnlyList<PrinterQueueInfo> queues = EnumerateQueues();
        IReadOnlyList<PnpPrinterDevice> devices = EnumeratePnpPrinterDevices();
        IReadOnlyList<PrinterDeviceCorrelation> correlations = Correlate(queues, devices);
        return new PrinterDiscoveryReport(DateTimeOffset.UtcNow, queues, devices, correlations);
    }

    public IReadOnlyList<PrinterQueueInfo> EnumerateQueues()
    {
        uint flags = NativeMethods.PrinterEnumLocal | NativeMethods.PrinterEnumConnections;
        bool initialResult = NativeMethods.EnumPrinters(
            flags,
            null,
            2,
            IntPtr.Zero,
            0,
            out uint required,
            out _);
        int error = Marshal.GetLastWin32Error();
        if (required == 0)
        {
            if (initialResult || error is 0 or NativeMethods.ErrorInsufficientBuffer)
            {
                return [];
            }

            throw new Win32Exception(error, "Unable to determine the printer enumeration buffer size.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!NativeMethods.EnumPrinters(flags, null, 2, buffer, required, out _, out uint returned))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate printers.");
            }

            int itemSize = Marshal.SizeOf<NativeMethods.PrinterInfo2>();
            List<PrinterQueueInfo> queues = new(checked((int)returned));
            for (int index = 0; index < returned; index++)
            {
                IntPtr item = IntPtr.Add(buffer, checked(index * itemSize));
                NativeMethods.PrinterInfo2 info = Marshal.PtrToStructure<NativeMethods.PrinterInfo2>(item);
                string name = RequiredString(info.PrinterName, "printer name");
                queues.Add(new PrinterQueueInfo(
                    name,
                    OptionalString(info.ShareName),
                    OptionalString(info.PortName) ?? string.Empty,
                    OptionalString(info.DriverName) ?? string.Empty,
                    OptionalString(info.PrintProcessor) ?? string.Empty,
                    OptionalString(info.DataType) ?? string.Empty,
                    OptionalString(info.Location),
                    OptionalString(info.Comment),
                    (WindowsPrinterStatus)info.Status,
                    info.Attributes,
                    info.Jobs,
                    TryGetDriver(name)));
            }

            return queues.OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public PrinterCapabilities GetCapabilities(PrinterQueueInfo queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        return new PrinterCapabilities(
            queue.Name,
            queue.PortName,
            QuerySingle(queue, NativeMethods.DcFields),
            QuerySingle(queue, NativeMethods.DcMinExtent),
            QuerySingle(queue, NativeMethods.DcMaxExtent),
            QuerySingle(queue, NativeMethods.DcDuplex) == 1,
            QuerySingle(queue, NativeMethods.DcColorDevice) == 1,
            QuerySingle(queue, NativeMethods.DcCopies),
            QueryResolutions(queue),
            QueryFixedStrings(queue, NativeMethods.DcMediaReady, 64),
            QueryFixedStrings(queue, NativeMethods.DcPaperNames, 64),
            QueryCapabilityString(queue, NativeMethods.DcManufacturer),
            QueryCapabilityString(queue, NativeMethods.DcModel));
    }

    public IReadOnlyList<PnpPrinterDevice> EnumeratePnpPrinterDevices()
    {
        Guid classGuid = NativeMethods.PrinterClassGuid;
        using SafeDeviceInfoSetHandle devices = NativeMethods.SetupDiGetClassDevs(
            ref classGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DigcfPresent);

        if (devices.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate printer PnP devices.");
        }

        List<PnpPrinterDevice> result = [];
        for (uint index = 0; ; index++)
        {
            NativeMethods.SpDevInfoData data = new()
            {
                Size = checked((uint)Marshal.SizeOf<NativeMethods.SpDevInfoData>()),
            };

            if (!NativeMethods.SetupDiEnumDeviceInfo(devices, index, ref data))
            {
                const int noMoreItems = 259;
                int error = Marshal.GetLastWin32Error();
                if (error == noMoreItems)
                {
                    break;
                }

                throw new Win32Exception(error, "Unable to enumerate a printer PnP device.");
            }

            result.Add(new PnpPrinterDevice(
                GetDeviceInstanceId(devices, ref data),
                GetDeviceProperty(devices, ref data, NativeMethods.SpdrpFriendlyName).FirstOrDefault(),
                GetDeviceProperty(devices, ref data, NativeMethods.SpdrpManufacturer).FirstOrDefault(),
                GetDeviceProperty(devices, ref data, NativeMethods.SpdrpDeviceDescription).FirstOrDefault(),
                GetDeviceProperty(devices, ref data, NativeMethods.SpdrpLocationInformation).FirstOrDefault(),
                GetDeviceProperty(devices, ref data, NativeMethods.SpdrpHardwareId)));
        }

        return result.OrderBy(device => device.FriendlyName ?? device.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static PrinterDriverInfo? TryGetDriver(string printerName)
    {
        if (!NativeMethods.OpenPrinter(printerName, out SafePrinterHandle printer, IntPtr.Zero))
        {
            return null;
        }

        using (printer)
        {
            NativeMethods.GetPrinterDriver(printer, null, 2, IntPtr.Zero, 0, out uint required);
            if (required == 0)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                if (!NativeMethods.GetPrinterDriver(printer, null, 2, buffer, required, out _))
                {
                    return null;
                }

                NativeMethods.DriverInfo2 info = Marshal.PtrToStructure<NativeMethods.DriverInfo2>(buffer);
                string driverPath = OptionalString(info.DriverPath) ?? string.Empty;
                string? fileVersion = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(driverPath) && File.Exists(driverPath))
                    {
                        fileVersion = FileVersionInfo.GetVersionInfo(driverPath).FileVersion;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    fileVersion = null;
                }

                return new PrinterDriverInfo(
                    info.Version,
                    OptionalString(info.Name) ?? string.Empty,
                    OptionalString(info.Environment) ?? string.Empty,
                    driverPath,
                    OptionalString(info.DataFile) ?? string.Empty,
                    OptionalString(info.ConfigurationFile) ?? string.Empty,
                    fileVersion);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static IReadOnlyList<PrinterDeviceCorrelation> Correlate(
        IReadOnlyList<PrinterQueueInfo> queues,
        IReadOnlyList<PnpPrinterDevice> devices)
    {
        List<PrinterDeviceCorrelation> correlations = [];
        foreach (PrinterQueueInfo queue in queues)
        {
            bool queueLooksLikeQl800 = ContainsQl800(queue.Name) || ContainsQl800(queue.DriverName);
            PnpPrinterDevice[] candidates = devices.Where(IsQl800Device).ToArray();

            if (!queueLooksLikeQl800 || candidates.Length == 0)
            {
                correlations.Add(new PrinterDeviceCorrelation(
                    queue.Name,
                    null,
                    CorrelationConfidence.None,
                    ["No QL-800 model match was found in both the queue and PnP device data."]));
                continue;
            }

            if (candidates.Length == 1)
            {
                PnpPrinterDevice device = candidates[0];
                List<string> evidence =
                [
                    $"Queue or driver identifies QL-800: '{queue.Name}' / '{queue.DriverName}'.",
                    $"One present PnP printer matches QL-800: '{device.FriendlyName ?? device.InstanceId}'.",
                ];
                if (device.HardwareIds.Any(IsQl800HardwareId))
                {
                    evidence.Add("PnP hardware ID contains Brother VID 04F9 and QL-800 PID 209B.");
                }

                correlations.Add(new PrinterDeviceCorrelation(
                    queue.Name,
                    device.InstanceId,
                    CorrelationConfidence.High,
                    evidence));
            }
            else
            {
                correlations.Add(new PrinterDeviceCorrelation(
                    queue.Name,
                    null,
                    CorrelationConfidence.Ambiguous,
                    [$"{candidates.Length} present QL-800-like PnP devices match this queue; the spooler does not expose a unique device instance mapping."]));
            }
        }

        return correlations;
    }

    private static int QuerySingle(PrinterQueueInfo queue, short capability) =>
        NativeMethods.DeviceCapabilities(queue.Name, queue.PortName, capability, IntPtr.Zero, IntPtr.Zero);

    private static IReadOnlyList<PrinterResolution> QueryResolutions(PrinterQueueInfo queue)
    {
        int count = QuerySingle(queue, NativeMethods.DcEnumResolutions);
        if (count <= 0)
        {
            return [];
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked(count * 2 * sizeof(int)));
        try
        {
            int actual = NativeMethods.DeviceCapabilities(
                queue.Name,
                queue.PortName,
                NativeMethods.DcEnumResolutions,
                buffer,
                IntPtr.Zero);
            if (actual <= 0)
            {
                return [];
            }

            List<PrinterResolution> values = new(actual);
            for (int index = 0; index < actual; index++)
            {
                values.Add(new PrinterResolution(
                    Marshal.ReadInt32(buffer, index * 2 * sizeof(int)),
                    Marshal.ReadInt32(buffer, ((index * 2) + 1) * sizeof(int))));
            }

            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> QueryFixedStrings(
        PrinterQueueInfo queue,
        short capability,
        int charactersPerEntry)
    {
        int count = QuerySingle(queue, capability);
        if (count <= 0)
        {
            return [];
        }

        int bytesPerEntry = checked(charactersPerEntry * sizeof(char));
        IntPtr buffer = Marshal.AllocHGlobal(checked(count * bytesPerEntry));
        try
        {
            int actual = NativeMethods.DeviceCapabilities(
                queue.Name,
                queue.PortName,
                capability,
                buffer,
                IntPtr.Zero);
            if (actual <= 0)
            {
                return [];
            }

            List<string> values = new(actual);
            for (int index = 0; index < actual; index++)
            {
                string? value = Marshal.PtrToStringUni(IntPtr.Add(buffer, index * bytesPerEntry), charactersPerEntry);
                int terminator = value?.IndexOf('\0', StringComparison.Ordinal) ?? -1;
                if (terminator >= 0)
                {
                    value = value?[..terminator];
                }

                value = value?.TrimEnd(' ');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? QueryCapabilityString(PrinterQueueInfo queue, short capability)
    {
        const int characterCapacity = 256;
        IntPtr buffer = Marshal.AllocHGlobal(characterCapacity * sizeof(char));
        try
        {
            Span<byte> clear = new byte[characterCapacity * sizeof(char)];
            Marshal.Copy(clear.ToArray(), 0, buffer, clear.Length);
            int result = NativeMethods.DeviceCapabilities(
                queue.Name,
                queue.PortName,
                capability,
                buffer,
                IntPtr.Zero);
            return result < 0 ? null : Marshal.PtrToStringUni(buffer)?.TrimEnd('\0', ' ');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetDeviceInstanceId(
        SafeDeviceInfoSetHandle devices,
        ref NativeMethods.SpDevInfoData data)
    {
        NativeMethods.SetupDiGetDeviceInstanceId(devices, ref data, null, 0, out uint required);
        if (required == 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[required];
        if (!NativeMethods.SetupDiGetDeviceInstanceId(devices, ref data, buffer, required, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read a printer device instance ID.");
        }

        return new string(buffer).TrimEnd('\0');
    }

    private static IReadOnlyList<string> GetDeviceProperty(
        SafeDeviceInfoSetHandle devices,
        ref NativeMethods.SpDevInfoData data,
        uint property)
    {
        NativeMethods.SetupDiGetDeviceRegistryProperty(
            devices,
            ref data,
            property,
            out _,
            null,
            0,
            out uint required);

        if (required == 0)
        {
            return [];
        }

        byte[] buffer = new byte[required];
        if (!NativeMethods.SetupDiGetDeviceRegistryProperty(
                devices,
                ref data,
                property,
                out _,
                buffer,
                checked((uint)buffer.Length),
                out _))
        {
            return [];
        }

        string decoded = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return decoded.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsQl800Device(PnpPrinterDevice device) =>
        ContainsQl800(device.FriendlyName) ||
        ContainsQl800(device.DeviceDescription) ||
        device.HardwareIds.Any(IsQl800HardwareId) ||
        device.HardwareIds.Any(ContainsQl800);

    private static bool IsQl800HardwareId(string value) =>
        value.Contains("VID_04F9", StringComparison.OrdinalIgnoreCase) &&
        value.Contains("PID_209B", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsQl800(string? value) =>
        value?.Contains("QL-800", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Contains("QL800", StringComparison.OrdinalIgnoreCase) == true;

    private static string RequiredString(IntPtr value, string field) =>
        OptionalString(value) ?? throw new InvalidDataException($"Windows returned no {field}.");

    private static string? OptionalString(IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUni(value);
}
