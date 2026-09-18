namespace LabelStudio.Printing;

public interface IPrinterBackend
{
    PrinterDescriptor Descriptor { get; }
    PrinterCapabilities Capabilities { get; }
    PrintResult Print(DevicePrintJob job);
}