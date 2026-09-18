namespace LabelStudio.Printing;

public sealed record MediaProfile(
    string ProfileId,
    string Sku,
    string DisplayName,
    MediaKind Kind,
    int PhysicalWidthMicrometres,
    int? PhysicalLengthMicrometres,
    PrintableArea PrintableArea,
    IReadOnlyList<ThermalInk> SupportedInks,
    IReadOnlyList<string> CompatiblePrinterModels,
    CutterConstraints Cutter)
{
    public bool SupportsRed => SupportedInks.Contains(ThermalInk.Red);
}