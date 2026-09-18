namespace LabelStudio.Printing;

public sealed record PrinterResolution(int DpiX, int DpiY)
{
    public bool IsSquare => DpiX == DpiY;
    public override string ToString() => $"{DpiX}x{DpiY}";
}

public sealed record PrinterCapabilities(
    bool SupportsCutter,
    bool SupportsTwoColour,
    IReadOnlyList<PrinterResolution> Resolutions,
    IReadOnlyList<string> SupportedMediaProfileIds)
{
    public static PrinterCapabilities Unknown => new(
        false, false, Array.Empty<PrinterResolution>(), Array.Empty<string>());
}