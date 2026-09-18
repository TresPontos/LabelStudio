namespace LabelStudio.Printing;

public sealed record PrintResult(
    bool Success,
    string? JobId,
    string? Error,
    DateTimeOffset CompletedAtUtc)
{
    public static PrintResult Succeeded(string? jobId) =>
        new(true, jobId, null, DateTimeOffset.UtcNow);

    public static PrintResult Failed(string error) =>
        new(false, null, error, DateTimeOffset.UtcNow);
}