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

    public int CurrentVersion => LabelDocument.CurrentFormatVersion;

    public void Register(IDocumentMigration migration)
    {
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
            version = migration.ToVersion;
        }

        return documentJson;
    }
}