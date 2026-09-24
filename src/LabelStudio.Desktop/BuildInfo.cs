using System.Reflection;

namespace LabelStudio.Desktop;

public sealed record BuildInfo
{
    public string Version { get; }
    public string? Commit { get; }
    public bool Dirty { get; }
    public DateTimeOffset? BuiltAtUtc { get; }
    public string Configuration { get; }

    private BuildInfo(string version, string? commit, bool dirty, DateTimeOffset? builtAtUtc, string configuration)
    {
        Version = version;
        Commit = commit;
        Dirty = dirty;
        BuiltAtUtc = builtAtUtc;
        Configuration = configuration;
    }

    public string ShortVersion => Commit is null
        ? $"v{Version}"
        : $"v{Version} · {(Dirty ? Commit + "-dirty" : Commit)}";

    public string DisplayVersion => BuiltAtUtc is { } built
        ? $"{ShortVersion} · {built.UtcDateTime:yyyy-MM-dd HH:mm} UTC"
        : ShortVersion;

    public static BuildInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        string configuration = assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? "Release";

        string version = informational;
        string? commit = null;
        bool dirty = false;
        DateTimeOffset? builtAtUtc = null;

        int metadataStart = informational.IndexOf('+');
        if (metadataStart >= 0)
        {
            version = informational[..metadataStart];
            string[] parts = informational[(metadataStart + 1)..].Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] is "commit")
                {
                    commit = parts[i + 1];
                }
                else if (parts[i] is "commitdirty")
                {
                    commit = parts[i + 1];
                    dirty = true;
                }
                else if (parts[i] is "built" &&
                    DateTimeOffset.TryParseExact(
                        parts[i + 1],
                        "yyyyMMddHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal,
                        out DateTimeOffset parsed))
                {
                    builtAtUtc = parsed;
                }
            }
        }

        return new BuildInfo(version, commit, dirty, builtAtUtc, configuration);
    }

    public static BuildInfo Current => FromAssembly(typeof(BuildInfo).Assembly);
}
