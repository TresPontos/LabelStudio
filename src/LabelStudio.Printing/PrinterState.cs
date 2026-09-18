namespace LabelStudio.Printing;

public sealed record PrinterState(
    bool IsOnline,
    string? CurrentMediaProfileId,
    string? CoverState,
    string? CurrentError,
    string? CurrentJobId)
{
    public static PrinterState Unknown => new(false, null, null, null, null);
}