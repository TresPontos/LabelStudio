using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabelStudio.Storage.Migrations;

internal sealed class DocumentMigrationV3ToV4 : IDocumentMigration
{
    private const int DefaultContinuousLengthMicrometres = 48_260;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public int FromVersion => 3;
    public int ToVersion => 4;

    public string Migrate(string documentJson)
    {
        JsonObject root = JsonNode.Parse(documentJson)?.AsObject()
            ?? throw new InvalidDataException("Document JSON root is not an object.");
        JsonObject dimensions = root["pageDimensions"] as JsonObject ?? new JsonObject
        {
            ["widthMicrometres"] = 0,
            ["heightMicrometres"] = 0,
        };
        root["pageDimensions"] = dimensions;

        int height = dimensions["heightMicrometres"]?.GetValue<int>() ?? 0;
        bool continuous = height == 0;
        if (continuous)
        {
            height = DefaultContinuousLengthMicrometres;
            dimensions["heightMicrometres"] = height;

            if (root["mediaGeometry"] is JsonObject mediaGeometry)
            {
                if (mediaGeometry["physicalDimensions"] is JsonObject physicalDimensions)
                {
                    physicalDimensions["heightMicrometres"] = height;
                }

                if (mediaGeometry["printableArea"] is JsonObject printableArea)
                {
                    bool brotherQl = string.Equals(
                        root["mediaProfileId"]?.GetValue<string>(),
                        "brother.dk-22251",
                        StringComparison.OrdinalIgnoreCase);
                    int feedMargin = brotherQl ? 2_963 : 0;
                    printableArea["y"] = feedMargin;
                    printableArea["height"] = Math.Max(0, height - feedMargin * 2);
                }
            }
        }

        root["mediaKind"] = continuous ? "continuous" : "dieCut";

        JsonObject designMetadata = root["designMetadata"] as JsonObject ?? new JsonObject();
        root["designMetadata"] = designMetadata;
        designMetadata["safeMargins"] = new JsonObject
        {
            ["top"] = 1_000,
            ["right"] = 1_000,
            ["bottom"] = 1_000,
            ["left"] = 1_000,
        };

        root["formatVersion"] = 4;
        return root.ToJsonString(Options);
    }
}
