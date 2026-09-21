using System.Text.Json.Nodes;
using LabelStudio.Document;

namespace LabelStudio.Storage.Migrations;

public interface IDocumentMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    string Migrate(string documentJson);
}

public sealed class UnsupportedDocumentVersionException : Exception
{
    public int AttemptedVersion { get; }

    public UnsupportedDocumentVersionException(int version)
        : base($"Document format version {version} is not supported. Current supported version is {LabelDocument.CurrentFormatVersion}.")
    {
        AttemptedVersion = version;
    }
}

public sealed class DocumentMigrator
{
    private readonly Dictionary<int, IDocumentMigration> _migrationsByFromVersion = new();

    public DocumentMigrator()
    {
        Register(new DocumentMigrationV1ToV2());
    }

    public int CurrentVersion => LabelDocument.CurrentFormatVersion;

    public void Register(IDocumentMigration migration)
    {
        if (migration.ToVersion != migration.FromVersion + 1)
        {
            throw new ArgumentException("A document migration must advance exactly one version.", nameof(migration));
        }

        _migrationsByFromVersion[migration.FromVersion] = migration;
    }

    public string MigrateToCurrent(string documentJson)
    {
        int version = DocumentJsonSerializer.ReadFormatVersion(documentJson);

        if (version > CurrentVersion)
        {
            throw new UnsupportedDocumentVersionException(version);
        }

        while (version < CurrentVersion)
        {
            if (!_migrationsByFromVersion.TryGetValue(version, out IDocumentMigration? migration))
            {
                throw new InvalidOperationException(
                    $"No migration registered from version {version} to {version + 1}.");
            }

            documentJson = migration.Migrate(documentJson);
            int migratedVersion = DocumentJsonSerializer.ReadFormatVersion(documentJson);
            if (migratedVersion != version + 1 || migratedVersion != migration.ToVersion)
            {
                throw new InvalidOperationException(
                    $"Migration from version {version} did not advance exactly one version.");
            }

            version = migratedVersion;
        }

        return documentJson;
    }
}
