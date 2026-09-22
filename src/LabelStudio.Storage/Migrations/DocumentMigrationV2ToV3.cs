using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabelStudio.Storage.Migrations;

internal sealed class DocumentMigrationV2ToV3 : IDocumentMigration
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public int FromVersion => 2;
    public int ToVersion => 3;

    public string Migrate(string documentJson)
    {
        JsonObject root = JsonNode.Parse(documentJson)?.AsObject()
            ?? throw new InvalidDataException("Document JSON root is not an object.");

        if (root["elements"] is JsonArray elements)
        {
            foreach (JsonObject element in elements.OfType<JsonObject>()
                .Where(element => element["type"]?.GetValue<string>() == "text"))
            {
                element["frameSizing"] = "fixed";
                element["wrapping"] = "noWrap";
                element["overflow"] = "clip";
                element["horizontalAlignment"] = "left";
                element["verticalAlignment"] = "top";
            }
        }

        root["formatVersion"] = 3;
        return root.ToJsonString(Options);
    }
}
