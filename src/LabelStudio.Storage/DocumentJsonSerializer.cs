using System.Text.Json;
using System.Text.Json.Nodes;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Storage;

internal static class DocumentJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize(LabelDocument document)
    {
        JsonObject root = new()
        {
            ["formatVersion"] = document.FormatVersion,
            ["id"] = document.Id.ToString(),
            ["pageDimensions"] = SerializePhysicalSize(document.PageDimensions),
            ["mediaProfileId"] = document.MediaProfileId,
            ["mediaGeometry"] = SerializeMediaSnapshot(document.MediaGeometry),
            ["printDefaults"] = SerializePrintDefaults(document.PrintDefaults),
            ["designMetadata"] = SerializeDesignMetadata(document.DesignMetadata),
            ["elements"] = new JsonArray(
                document.Elements.Select(SerializeElement).ToArray()),
        };
        return root.ToJsonString(Options);
    }

    public static LabelDocument Deserialize(string json)
    {
        JsonObject root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Document JSON root is not an object.");

        int formatVersion = root["formatVersion"]?.GetValue<int>()
            ?? throw new InvalidDataException("Missing formatVersion.");

        DocumentId id = DocumentId.Parse(root["id"]?.GetValue<string>()
            ?? throw new InvalidDataException("Missing document id."));

        PhysicalSize pageDimensions = DeserializePhysicalSize(root["pageDimensions"]?.AsObject());
        string mediaProfileId = root["mediaProfileId"]?.GetValue<string>()
            ?? throw new InvalidDataException("Missing mediaProfileId.");
        MediaSnapshot mediaGeometry = DeserializeMediaSnapshot(root["mediaGeometry"]?.AsObject());
        DocumentPrintDefaults printDefaults = DeserializePrintDefaults(root["printDefaults"]?.AsObject());
        DocumentDesignMetadata designMetadata = DeserializeDesignMetadata(root["designMetadata"]?.AsObject());

        JsonArray? elementArray = root["elements"]?.AsArray();
        List<DocumentElement> elements = [];
        if (elementArray is not null)
        {
            foreach (JsonNode? node in elementArray)
            {
                if (node is JsonObject obj)
                {
                    elements.Add(DeserializeElement(obj));
                }
            }
        }

        return new LabelDocument(
            id,
            formatVersion,
            pageDimensions,
            mediaProfileId,
            mediaGeometry,
            elements.AsReadOnly(),
            printDefaults,
            designMetadata);
    }

    public static int ReadFormatVersion(string json)
    {
        JsonObject root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Document JSON root is not an object.");
        return root["formatVersion"]?.GetValue<int>()
            ?? throw new InvalidDataException("Missing formatVersion.");
    }

    private static JsonObject SerializePhysicalSize(PhysicalSize size) => new()
    {
        ["widthMicrometres"] = size.Width.Value,
        ["heightMicrometres"] = size.Height.Value,
    };

    private static PhysicalSize DeserializePhysicalSize(JsonObject? obj)
    {
        if (obj is null) throw new InvalidDataException("Missing physicalSize.");
        return new PhysicalSize(
            new Micrometre(obj["widthMicrometres"]?.GetValue<int>() ?? 0),
            new Micrometre(obj["heightMicrometres"]?.GetValue<int>() ?? 0));
    }

    private static JsonObject SerializeMediaSnapshot(MediaSnapshot snap) => new()
    {
        ["profileId"] = snap.ProfileId,
        ["physicalDimensions"] = SerializePhysicalSize(snap.PhysicalDimensions),
        ["printableArea"] = SerializeRect(snap.PrintableArea),
    };

    private static MediaSnapshot DeserializeMediaSnapshot(JsonObject? obj)
    {
        if (obj is null) throw new InvalidDataException("Missing mediaGeometry.");
        return new MediaSnapshot(
            obj["profileId"]?.GetValue<string>() ?? "",
            DeserializePhysicalSize(obj["physicalDimensions"]?.AsObject()),
            DeserializeRect(obj["printableArea"]?.AsObject()));
    }

    private static JsonObject SerializePrintDefaults(DocumentPrintDefaults defaults) => new()
    {
        ["defaultInk"] = defaults.DefaultInk.ToString().ToLowerInvariant(),
        ["autoCut"] = defaults.AutoCut,
        ["cutAtEnd"] = defaults.CutAtEnd,
        ["dpi"] = defaults.Dpi,
    };

    private static DocumentPrintDefaults DeserializePrintDefaults(JsonObject? obj)
    {
        if (obj is null) return DocumentPrintDefaults.Default;
        return new DocumentPrintDefaults(
            Enum.TryParse<InkChannel>(obj["defaultInk"]?.GetValue<string>(), true, out var ink)
                ? ink : InkChannel.Black,
            obj["autoCut"]?.GetValue<bool>() ?? true,
            obj["cutAtEnd"]?.GetValue<bool>() ?? true,
            obj["dpi"]?.GetValue<int>() ?? 300);
    }

    private static JsonObject SerializeDesignMetadata(DocumentDesignMetadata metadata) => new()
    {
        ["groups"] = new JsonArray(metadata.Groups.Select(group => new JsonObject
        {
            ["id"] = group.Id,
            ["name"] = group.Name,
            ["memberIds"] = new JsonArray(group.MemberIds.Select(id => JsonValue.Create(id)).ToArray()),
            ["visible"] = group.IsVisible,
            ["locked"] = group.IsLocked,
        }).ToArray()),
        ["guides"] = new JsonArray(metadata.Guides.Select(guide => new JsonObject
        {
            ["id"] = guide.Id,
            ["orientation"] = guide.Orientation.ToString().ToLowerInvariant(),
            ["position"] = guide.Position.Value,
            ["name"] = guide.Name,
        }).ToArray()),
        ["grid"] = new JsonObject
        {
            ["xSpacing"] = metadata.Grid.XSpacing.Value,
            ["ySpacing"] = metadata.Grid.YSpacing.Value,
            ["origin"] = SerializePoint(metadata.Grid.Origin),
            ["majorInterval"] = metadata.Grid.MajorInterval,
        },
    };

    private static DocumentDesignMetadata DeserializeDesignMetadata(JsonObject? obj)
    {
        if (obj is null) return DocumentDesignMetadata.Default;

        List<ElementGroup> groups = [];
        if (obj["groups"] is JsonArray groupArray)
        {
            foreach (JsonObject group in groupArray.OfType<JsonObject>())
            {
                IReadOnlyList<string> memberIds = group["memberIds"] is JsonArray memberArray
                    ? memberArray.Select(node => node?.GetValue<string>() ?? "").ToList().AsReadOnly()
                    : Array.Empty<string>();
                groups.Add(new ElementGroup(
                    group["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D"),
                    group["name"]?.GetValue<string>(),
                    memberIds,
                    group["visible"]?.GetValue<bool>() ?? true,
                    group["locked"]?.GetValue<bool>() ?? false));
            }
        }

        List<DocumentGuide> guides = [];
        if (obj["guides"] is JsonArray guideArray)
        {
            foreach (JsonObject guide in guideArray.OfType<JsonObject>())
            {
                guides.Add(new DocumentGuide(
                    guide["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D"),
                    Enum.TryParse(guide["orientation"]?.GetValue<string>(), true, out DocumentGuideOrientation orientation)
                        ? orientation : DocumentGuideOrientation.Horizontal,
                    new Micrometre(guide["position"]?.GetValue<int>() ?? 0),
                    guide["name"]?.GetValue<string>()));
            }
        }

        JsonObject? grid = obj["grid"]?.AsObject();
        DocumentGridGeometry gridGeometry = grid is null
            ? DocumentGridGeometry.Default
            : new DocumentGridGeometry(
                new Micrometre(grid["xSpacing"]?.GetValue<int>() ?? DocumentGridGeometry.Default.XSpacing.Value),
                new Micrometre(grid["ySpacing"]?.GetValue<int>() ?? DocumentGridGeometry.Default.YSpacing.Value),
                DeserializePoint(grid["origin"]?.AsObject()),
                grid["majorInterval"]?.GetValue<int>() ?? DocumentGridGeometry.Default.MajorInterval);

        return new DocumentDesignMetadata(groups.AsReadOnly(), guides.AsReadOnly(), gridGeometry);
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

    private static JsonObject SerializeElement(DocumentElement element)
    {
        JsonObject obj = new()
        {
            ["id"] = element.Id,
            ["type"] = element.ElementType,
            ["bounds"] = SerializeRect(element.Bounds),
            ["ink"] = element.Ink.ToString().ToLowerInvariant(),
            ["name"] = element.Name,
            ["visible"] = element.IsVisible,
            ["locked"] = element.IsLocked,
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
            IsVisible = obj["visible"]?.GetValue<bool>() ?? true,
            IsLocked = obj["locked"]?.GetValue<bool>() ?? false,
            RotationMillidegrees = obj["rotationMillidegrees"]?.GetValue<int>() ?? 0,
        };
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
