namespace LabelStudio.Printing;

public sealed record PrinterDescriptor(
    string Id,
    string DisplayName,
    string Manufacturer,
    string Model)
{
    public bool IsBrotherQl => Model.StartsWith("QL-", StringComparison.OrdinalIgnoreCase);
}