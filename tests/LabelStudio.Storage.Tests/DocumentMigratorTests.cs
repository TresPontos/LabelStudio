using LabelStudio.Storage.Migrations;

namespace LabelStudio.Storage.Tests;

public class DocumentMigratorTests
{
    [Fact]
    public void MigrateToCurrent_Version1_ReturnsUnchanged()
    {
        string json = """
        {
            "formatVersion": 1,
            "id": "00000000-0000-0000-0000-000000000001",
            "pageDimensions": { "widthMicrometres": 62000, "heightMicrometres": 0 },
            "mediaProfileId": "brother.dk-22251",
            "mediaGeometry": {
                "profileId": "brother.dk-22251",
                "physicalDimensions": { "widthMicrometres": 62000, "heightMicrometres": 0 },
                "printableArea": { "x": 1500, "y": 0, "width": 58900, "height": 0 }
            },
            "printDefaults": { "defaultInk": "black", "autoCut": true, "cutAtEnd": true, "dpi": 300 },
            "elements": []
        }
        """;

        DocumentMigrator migrator = new();
        string result = migrator.MigrateToCurrent(json);
        Assert.Equal(json, result);
    }

    [Fact]
    public void MigrateToCurrent_FutureVersion_Throws()
    {
        string json = """{"formatVersion": 99, "id": "test"}""";
        DocumentMigrator migrator = new();

        Assert.Throws<UnsupportedDocumentVersionException>(() => migrator.MigrateToCurrent(json));
    }

    [Fact]
    public void MigrateToCurrent_AppliesMigrationChain()
    {
        string v0Json = """{"formatVersion": 0, "oldField": "value"}""";

        StubMigration0to1 stub = new();
        DocumentMigrator migrator = new();
        migrator.Register(stub);

        string result = migrator.MigrateToCurrent(v0Json);
        Assert.Contains("formatVersion\": 1", result);
        Assert.Contains("oldField", result);
    }

    [Fact]
    public void MigrateToCurrent_MissingMigration_Throws()
    {
        string v0Json = """{"formatVersion": 0}""";
        DocumentMigrator migrator = new();

        Assert.Throws<InvalidOperationException>(() => migrator.MigrateToCurrent(v0Json));
    }

    private sealed class StubMigration0to1 : IDocumentMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;

        public string Migrate(string documentJson)
        {
            return documentJson.Replace("formatVersion\": 0", "formatVersion\": 1");
        }
    }
}