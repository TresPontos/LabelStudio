using System.Runtime.InteropServices;

namespace Ql800Spike.Core.Diagnostics;

public sealed record EnvironmentManifest(
    DateTimeOffset CapturedAtUtc,
    string OperatingSystem,
    string OsArchitecture,
    string ProcessArchitecture,
    string Framework,
    string RuntimeIdentifier,
    bool Is64BitProcess,
    string MachineName,
    string UserName,
    string ApplicationVersion)
{
    public static EnvironmentManifest Capture()
    {
        Version? version = typeof(EnvironmentManifest).Assembly.GetName().Version;
        return new EnvironmentManifest(
            DateTimeOffset.UtcNow,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier,
            Environment.Is64BitProcess,
            Environment.MachineName,
            Environment.UserName,
            version?.ToString() ?? "unknown");
    }
}
