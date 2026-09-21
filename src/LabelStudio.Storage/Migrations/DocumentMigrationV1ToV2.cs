using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabelStudio.Storage.Migrations;

internal sealed class DocumentMigrationV1ToV2 : IDocumentMigration
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public int FromVersion => 1;
    public int ToVersion => 2;

    public string Migrate(string documentJson)
    {
        JsonObject root = JsonNode.Parse(documentJson)?.AsObject()
            ?? throw new InvalidDataException("Document JSON root is not an object.");

        if (root["elements"] is JsonArray elements)
        {
            foreach (JsonObject element in elements.OfType<JsonObject>())
            {
                element["name"] = null;
                element["visible"] = true;
                element["locked"] = false;
                element["rotationMillidegrees"] = 0;
                if (element["type"]?.GetValue<string>() == "image")
                {
                    element["lockAspectRatio"] = true;
                }
            }
        }

        root["designMetadata"] = new JsonObject
        {
            ["groups"] = new JsonArray(),
            ["guides"] = new JsonArray(),
            ["grid"] = new JsonObject
            {
                ["xSpacing"] = 1000,
                ["ySpacing"] = 1000,
                ["origin"] = new JsonObject { ["x"] = 0, ["y"] = 0 },
                ["majorInterval"] = 10,
            },
        };
        root["formatVersion"] = 2;
        return root.ToJsonString(Options);
    }
}
