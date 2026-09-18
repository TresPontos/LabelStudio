namespace LabelStudio.Printing;

public enum PreflightSeverity
{
    Error,
    Warning,
    Information,
}

public sealed record PreflightResult(
    IReadOnlyList<PreflightIssue> Errors,
    IReadOnlyList<PreflightIssue> Warnings,
    IReadOnlyList<PreflightIssue> Information)
{
    public bool HasErrors => Errors.Count > 0;
    public bool CanPrint => !HasErrors;

    public static PreflightResult Empty => new(
        Array.Empty<PreflightIssue>(),
        Array.Empty<PreflightIssue>(),
        Array.Empty<PreflightIssue>());
}

public sealed record PreflightIssue(
    PreflightSeverity Severity,
    string Code,
    string Message,
    string? ElementId = null);