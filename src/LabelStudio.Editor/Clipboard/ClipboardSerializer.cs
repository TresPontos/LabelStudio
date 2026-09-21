using System.Text.Json;
using System.Text.Json.Nodes;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Clipboard;

public sealed record ClipboardAsset(string SourceAssetId, string ContentHash, byte[] Bytes);

public sealed record ClipboardPayload(
    string Schema,
    int Version,
    string SourceDocumentId,
    IReadOnlyList<DocumentElement> Elements,
    IReadOnlyList<ElementGroup> Groups,
    IReadOnlyList<ClipboardAsset> Assets)
{
    public const string SchemaName = "labelstudio.clipboard";
    public const int CurrentVersion = 1;
    public const int MaxElements = 1000;
    public const int MaxGroups = 500;
    public const int MaxAssets = 100;
    public const int MaxAssetBytes = 25 * 1024 * 1024;
    public const int MaxTotalPayloadBytes = 100 * 1024 * 1024;
};

public static class ClipboardSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = new()
        {
            ["schema"] = payload.Schema,
            ["version"] = payload.Version,
            ["sourceDocumentId"] = payload.SourceDocumentId,
        };

        JsonArray elementsArray = new();
        foreach (DocumentElement element in payload.Elements)
        {
            elementsArray.Add(SerializeElement(element));
        }
        root["elements"] = elementsArray;

        JsonArray groupsArray = new();
        foreach (ElementGroup group in payload.Groups)
        {
            groupsArray.Add(new JsonObject
            {
                ["id"] = group.Id,
                ["name"] = group.Name,
                ["memberIds"] = new JsonArray(group.MemberIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
                ["isVisible"] = group.IsVisible,
                ["isLocked"] = group.IsLocked,
            });
        }
        root["groups"] = groupsArray;

        JsonArray assetsArray = new();
        foreach (ClipboardAsset asset in payload.Assets)
        {
            assetsArray.Add(new JsonObject
            {
                ["sourceAssetId"] = asset.SourceAssetId,
                ["contentHash"] = asset.ContentHash,
                ["bytes"] = Convert.ToBase64String(asset.Bytes),
            });
        }
        root["assets"] = assetsArray;

        return root.ToJsonString(Options);
    }

    public static ClipboardPayload Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonObject root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Clipboard JSON root is not an object.");

        string schema = root["schema"]?.GetValue<string>()
            ?? throw new InvalidDataException("Missing clipboard schema.");
        if (schema != ClipboardPayload.SchemaName)
        {
            throw new InvalidDataException($"Unsupported clipboard schema: {schema}.");
        }

        int version = root["version"]?.GetValue<int>()
            ?? throw new InvalidDataException("Missing clipboard version.");
        if (version > ClipboardPayload.CurrentVersion)
        {
            throw new InvalidDataException($"Clipboard version {version} is not supported.");
        }

        string sourceDocumentId = root["sourceDocumentId"]?.GetValue<string>() ?? string.Empty;

        List<DocumentElement> elements = [];
        if (root["elements"] is JsonArray elementArray)
        {
            if (elementArray.Count > ClipboardPayload.MaxElements)
            {
                throw new InvalidDataException($"Clipboard contains too many elements ({elementArray.Count}).");
            }
            foreach (JsonNode? node in elementArray)
            {
                if (node is JsonObject obj)
                {
                    elements.Add(DeserializeElement(obj));
                }
            }
        }

        List<ElementGroup> groups = [];
        if (root["groups"] is JsonArray groupArray)
        {
            if (groupArray.Count > ClipboardPayload.MaxGroups)
            {
                throw new InvalidDataException($"Clipboard contains too many groups ({groupArray.Count}).");
            }
            foreach (JsonNode? node in groupArray)
            {
                if (node is JsonObject obj)
                {
                    groups.Add(DeserializeGroup(obj));
                }
            }
        }

        List<ClipboardAsset> assets = [];
        if (root["assets"] is JsonArray assetArray)
        {
            if (assetArray.Count > ClipboardPayload.MaxAssets)
            {
                throw new InvalidDataException($"Clipboard contains too many assets ({assetArray.Count}).");
            }
            foreach (JsonNode? node in assetArray)
            {
                if (node is JsonObject obj)
                {
                    assets.Add(DeserializeAsset(obj));
                }
            }
        }

        return new ClipboardPayload(schema, version, sourceDocumentId, elements.AsReadOnly(), groups.AsReadOnly(), assets.AsReadOnly());
    }

    private static JsonObject SerializeElement(DocumentElement element)
    {
        JsonObject obj = new()
        {
            ["id"] = element.Id,
            ["type"] = element.ElementType,
            ["bounds"] = SerializeRect(element.Bounds),
            ["ink"] = element.Ink.ToString().ToLowerInvariant(),
            ["name"] = element.Name,
            ["isVisible"] = element.IsVisible,
            ["isLocked"] = element.IsLocked,
            ["rotationMillidegrees"] = element.RotationMillidegrees,
        };

        switch (element)
        {
            case RectangleElement rect:
                obj["fill"] = rect.Fill;
                obj["strokeWidth"] = rect.StrokeWidth.Value;
                break;
            case LineElement line:
                obj["start"] = SerializePoint(line.Start);
                obj["end"] = SerializePoint(line.End);
                obj["thickness"] = line.Thickness.Value;
                break;
            case ImageElement img:
                obj["assetId"] = img.AssetId;
                obj["lockAspectRatio"] = img.LockAspectRatio;
                break;
            case TextElement text:
                obj["text"] = text.Text;
                obj["fontSizePoints"] = text.FontSizePoints;
                obj["fontFamily"] = text.FontFamily;
                break;
        }

        return obj;
    }

    private static DocumentElement DeserializeElement(JsonObject obj)
    {
        string id = obj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D");
        string type = obj["type"]?.GetValue<string>() ?? "rectangle";
        MicrometreRect bounds = DeserializeRect(obj["bounds"]?.AsObject());
        InkChannel ink = Enum.TryParse<InkChannel>(obj["ink"]?.GetValue<string>(), true, out var i)
            ? i : InkChannel.Black;

        DocumentElement element = type switch
        {
            "rectangle" => new RectangleElement(
                id, bounds, ink,
                obj["fill"]?.GetValue<bool>() ?? true,
                new Micrometre(obj["strokeWidth"]?.GetValue<int>() ?? 0)),
            "line" => new LineElement(
                id,
                DeserializePoint(obj["start"]?.AsObject()),
                DeserializePoint(obj["end"]?.AsObject()),
                new Micrometre(obj["thickness"]?.GetValue<int>() ?? 0),
                ink),
            "image" => new ImageElement(
                id, bounds, ink,
                obj["assetId"]?.GetValue<string>() ?? "")
            {
                LockAspectRatio = obj["lockAspectRatio"]?.GetValue<bool>() ?? true,
            },
            "text" => new TextElement(
                id, bounds, ink,
                obj["text"]?.GetValue<string>() ?? "",
                obj["fontSizePoints"]?.GetValue<int>() ?? 12,
                obj["fontFamily"]?.GetValue<string>()),
            _ => throw new InvalidDataException($"Unknown element type: {type}"),
        };

        return element with
        {
            Name = obj["name"]?.GetValue<string>(),
            IsVisible = obj["isVisible"]?.GetValue<bool>() ?? true,
            IsLocked = obj["isLocked"]?.GetValue<bool>() ?? false,
            RotationMillidegrees = obj["rotationMillidegrees"]?.GetValue<int>() ?? 0,
        };
    }

    private static JsonObject SerializeGroup(ElementGroup group) => new()
    {
        ["id"] = group.Id,
        ["name"] = group.Name,
        ["memberIds"] = new JsonArray(group.MemberIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        ["isVisible"] = group.IsVisible,
        ["isLocked"] = group.IsLocked,
    };

    private static ElementGroup DeserializeGroup(JsonObject obj)
    {
        string id = obj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D");
        string? name = obj["name"]?.GetValue<string>();
        IReadOnlyList<string> memberIds = obj["memberIds"] is JsonArray memberArray
            ? memberArray.Select(node => node?.GetValue<string>() ?? "").ToList().AsReadOnly()
            : Array.Empty<string>();
        bool isVisible = obj["isVisible"]?.GetValue<bool>() ?? true;
        bool isLocked = obj["isLocked"]?.GetValue<bool>() ?? false;
        return new ElementGroup(id, name, memberIds, isVisible, isLocked);
    }

    private static ClipboardAsset DeserializeAsset(JsonObject obj)
    {
        string sourceAssetId = obj["sourceAssetId"]?.GetValue<string>()
            ?? throw new InvalidDataException("Clipboard asset missing sourceAssetId.");
        string contentHash = obj["contentHash"]?.GetValue<string>()
            ?? throw new InvalidDataException("Clipboard asset missing contentHash.");
        string base64 = obj["bytes"]?.GetValue<string>()
            ?? throw new InvalidDataException("Clipboard asset missing bytes.");

        byte[] bytes = Convert.FromBase64String(base64);
        if (bytes.Length > ClipboardPayload.MaxAssetBytes)
        {
            throw new InvalidDataException($"Clipboard asset '{sourceAssetId}' exceeds size limit.");
        }

        return new ClipboardAsset(sourceAssetId, contentHash, bytes);
    }

    private static JsonObject SerializeRect(MicrometreRect rect) => new()
    {
        ["x"] = rect.X.Value,
        ["y"] = rect.Y.Value,
        ["width"] = rect.Width.Value,
        ["height"] = rect.Height.Value,
    };

    private static MicrometreRect DeserializeRect(JsonObject? obj)
    {
        if (obj is null) return MicrometreRect.Zero;
        return new MicrometreRect(
            new Micrometre(obj["x"]?.GetValue<int>() ?? 0),
            new Micrometre(obj["y"]?.GetValue<int>() ?? 0),
            new Micrometre(obj["width"]?.GetValue<int>() ?? 0),
            new Micrometre(obj["height"]?.GetValue<int>() ?? 0));
    }

    private static JsonObject SerializePoint(MicrometrePoint pt) => new()
    {
        ["x"] = pt.X.Value,
        ["y"] = pt.Y.Value,
    };

    private static MicrometrePoint DeserializePoint(JsonObject? obj)
    {
        if (obj is null) return MicrometrePoint.Zero;
        return new MicrometrePoint(
            new Micrometre(obj["x"]?.GetValue<int>() ?? 0),
            new Micrometre(obj["y"]?.GetValue<int>() ?? 0));
    }
}