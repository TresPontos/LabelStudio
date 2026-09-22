using LabelStudio.Storage.Migrations;

namespace LabelStudio.Storage.Tests;

public class DocumentMigratorTests
{
    [Fact]
    public void MigrateToCurrent_Version1_AddsV2Defaults()
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
            "elements": [
                { "id": "r1", "type": "rectangle" },
                { "id": "i1", "type": "image", "assetId": "image.bin" }
            ]
        }
        """;

        DocumentMigrator migrator = new();
        string result = migrator.MigrateToCurrent(json);
        System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(result)!.AsObject();
        Assert.Equal(4, root["formatVersion"]!.GetValue<int>());
        Assert.Equal("continuous", root["mediaKind"]!.GetValue<string>());
        Assert.Equal(48_260, root["pageDimensions"]!["heightMicrometres"]!.GetValue<int>());
        Assert.Equal(48_260, root["mediaGeometry"]!["physicalDimensions"]!["heightMicrometres"]!.GetValue<int>());
        Assert.Equal(2_963, root["mediaGeometry"]!["printableArea"]!["y"]!.GetValue<int>());
        Assert.Equal(42_334, root["mediaGeometry"]!["printableArea"]!["height"]!.GetValue<int>());
        Assert.Equal(1_000, root["designMetadata"]!["safeMargins"]!["left"]!.GetValue<int>());
        Assert.NotNull(root["designMetadata"]);
        Assert.True(root["elements"]![0]!["visible"]!.GetValue<bool>());
        Assert.False(root["elements"]![0]!["locked"]!.GetValue<bool>());
        Assert.Equal(0, root["elements"]![0]!["rotationMillidegrees"]!.GetValue<int>());
        Assert.True(root["elements"]![1]!["lockAspectRatio"]!.GetValue<bool>());
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
        Assert.Contains("formatVersion\": 4", result);
        Assert.Contains("oldField", result);
    }

    [Fact]
    public void MigrateToCurrent_Version2Text_AddsFixedFrameDefaults()
    {
        string json = """
        {
            "formatVersion": 2,
            "elements": [
                { "id": "t1", "type": "text" },
                { "id": "r1", "type": "rectangle" }
            ]
        }
        """;

        string result = new DocumentMigrator().MigrateToCurrent(json);
        System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(result)!.AsObject();
        System.Text.Json.Nodes.JsonNode text = root["elements"]![0]!;

        Assert.Equal(4, root["formatVersion"]!.GetValue<int>());
        Assert.Equal("fixed", text["frameSizing"]!.GetValue<string>());
        Assert.Equal("noWrap", text["wrapping"]!.GetValue<string>());
        Assert.Equal("clip", text["overflow"]!.GetValue<string>());
        Assert.Equal("left", text["horizontalAlignment"]!.GetValue<string>());
        Assert.Equal("top", text["verticalAlignment"]!.GetValue<string>());
        Assert.Null(root["elements"]![1]!["frameSizing"]);
    }

    [Fact]
    public void MigrateToCurrent_Version3FinitePage_BecomesDieCutWithAdaptiveMargins()
    {
        string json = """
        {
            "formatVersion": 3,
            "pageDimensions": { "widthMicrometres": 17000, "heightMicrometres": 53900 },
            "designMetadata": { "groups": [], "guides": [], "grid": {} },
            "elements": []
        }
        """;

        System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(
            new DocumentMigrator().MigrateToCurrent(json))!.AsObject();

        Assert.Equal("dieCut", root["mediaKind"]!.GetValue<string>());
        Assert.Equal(53_900, root["pageDimensions"]!["heightMicrometres"]!.GetValue<int>());
        Assert.Equal(1_000, root["designMetadata"]!["safeMargins"]!["top"]!.GetValue<int>());
    }

    [Fact]
    public void Register_MigrationThatSkipsVersion_Throws()
    {
        DocumentMigrator migrator = new();

        Assert.Throws<ArgumentException>(() => migrator.Register(new StubMigration0to2()));
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

    private sealed class StubMigration0to2 : IDocumentMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 2;
        public string Migrate(string documentJson) => documentJson;
    }
}
