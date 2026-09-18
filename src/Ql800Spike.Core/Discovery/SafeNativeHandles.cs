using Microsoft.Win32.SafeHandles;

namespace Ql800Spike.Core.Discovery;

internal sealed class SafePrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePrinterHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.ClosePrinter(handle);
}

internal sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeDeviceInfoSetHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.SetupDiDestroyDeviceInfoList(handle);
}
