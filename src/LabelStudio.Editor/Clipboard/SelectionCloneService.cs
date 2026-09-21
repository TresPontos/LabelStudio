using System.Security.Cryptography;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor.Clipboard;

public sealed class PastePlan
{
    public IReadOnlyList<DocumentElement> NewElements { get; init; } = [];
    public IReadOnlyList<ElementGroup> NewGroups { get; init; } = [];
    public IReadOnlyDictionary<string, string> AssetRemap { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<(string SourceAssetId, string DestinationAssetId, byte[] Bytes)> ImportedAssets { get; init; } = [];
    public IReadOnlyList<SelectionTarget> NewSelectionTargets { get; init; } = [];
}

public sealed class SelectionCloneService
{
    public const int PasteOffsetMicrometresX = 2000;
    public const int PasteOffsetMicrometresY = 2000;

    private int _pasteCount;

    public void ResetPasteOffset() => _pasteCount = 0;

    public ClipboardPayload BuildPayload(
        LabelDocument document,
        IReadOnlyCollection<SelectionTarget> targets,
        Func<string, byte[]?>? assetBytesProvider = null)
    {
        HashSet<string> elementIds = new(StringComparer.Ordinal);
        HashSet<string> groupIds = new(StringComparer.Ordinal);

        foreach (SelectionTarget target in targets)
        {
            switch (target)
            {
                case SelectionTarget.ElementTarget elem:
                    elementIds.Add(elem.ElementId);
                    break;
                case SelectionTarget.GroupTarget grp:
                    groupIds.Add(grp.GroupId);
                    break;
            }
        }

        List<DocumentElement> elements = new();
        List<ElementGroup> groups = new();
        HashSet<string> includedAssetIds = new(StringComparer.Ordinal);

        foreach (string gid in groupIds)
        {
            ElementGroup? group = document.DesignMetadata.Groups
                .FirstOrDefault(g => string.Equals(g.Id, gid, StringComparison.Ordinal));
            if (group is null) continue;

            foreach (string memberId in group.MemberIds)
            {
                DocumentElement? element = document.Elements.FirstOrDefault(e => e.Id == memberId);
                if (element is null) continue;
                elements.Add(element);
                if (element is ImageElement img && !string.IsNullOrEmpty(img.AssetId))
                {
                    includedAssetIds.Add(img.AssetId);
                }
            }
            groups.Add(group);
        }

        foreach (string eid in elementIds)
        {
            DocumentElement? element = document.Elements.FirstOrDefault(e => e.Id == eid);
            if (element is null) continue;
            elements.Add(element);
            if (element is ImageElement img && !string.IsNullOrEmpty(img.AssetId))
            {
                includedAssetIds.Add(img.AssetId);
            }
        }

        List<ClipboardAsset> assets = new();
        if (assetBytesProvider is not null)
        {
            foreach (string assetId in includedAssetIds)
            {
                byte[]? bytes = assetBytesProvider(assetId);
                if (bytes is null || bytes.Length == 0) continue;
                string hash = ComputeHash(bytes);
                assets.Add(new ClipboardAsset(assetId, hash, bytes));
            }
        }

        return new ClipboardPayload(
            ClipboardPayload.SchemaName,
            ClipboardPayload.CurrentVersion,
            document.Id.Value.ToString(),
            elements.AsReadOnly(),
            groups.AsReadOnly(),
            assets.AsReadOnly());
    }

    public PastePlan PlanPaste(
        ClipboardPayload payload,
        LabelDocument destinationDocument,
        Func<string, byte[]?>? destinationAssetProvider = null,
        Func<string, string, byte[], string>? assetImporter = null,
        int? pasteCount = null)
    {
        int count = pasteCount ?? _pasteCount;
        int offsetX = PasteOffsetMicrometresX * (count + 1);
        int offsetY = PasteOffsetMicrometresY * (count + 1);

        Dictionary<string, string> elementIdRemap = new(StringComparer.Ordinal);
        Dictionary<string, string> groupIdRemap = new(StringComparer.Ordinal);
        Dictionary<string, string> assetIdRemap = new(StringComparer.Ordinal);

        foreach (DocumentElement element in payload.Elements)
        {
            elementIdRemap[element.Id] = Guid.NewGuid().ToString("D");
        }

        foreach (ElementGroup group in payload.Groups)
        {
            groupIdRemap[group.Id] = Guid.NewGuid().ToString("D");
        }

        List<(string SourceAssetId, string DestinationAssetId, byte[] Bytes)> importedAssets = new();
        Dictionary<string, string> existingContentHashes = new(StringComparer.Ordinal);

        if (destinationAssetProvider is not null)
        {
            foreach (ImageElement destImage in destinationDocument.Elements.OfType<ImageElement>())
            {
                if (string.IsNullOrEmpty(destImage.AssetId)) continue;
                byte[]? existingBytes = destinationAssetProvider(destImage.AssetId);
                if (existingBytes is null) continue;
                string hash = ComputeHash(existingBytes);
                existingContentHashes[hash] = destImage.AssetId;
            }
        }

        foreach (ClipboardAsset asset in payload.Assets)
        {
            string actualHash = ComputeHash(asset.Bytes);
            if (actualHash != asset.ContentHash)
            {
                throw new InvalidDataException(
                    $"Clipboard asset '{asset.SourceAssetId}' content hash mismatch.");
            }

            if (existingContentHashes.TryGetValue(actualHash, out string? existingAssetId))
            {
                assetIdRemap[asset.SourceAssetId] = existingAssetId;
            }
            else if (assetImporter is not null)
            {
                string newAssetId = assetImporter(asset.SourceAssetId, actualHash, asset.Bytes);
                assetIdRemap[asset.SourceAssetId] = newAssetId;
                importedAssets.Add((asset.SourceAssetId, newAssetId, asset.Bytes));
                existingContentHashes[actualHash] = newAssetId;
            }
            else
            {
                assetIdRemap[asset.SourceAssetId] = asset.SourceAssetId;
            }
        }

        List<DocumentElement> newElements = new();
        foreach (DocumentElement element in payload.Elements)
        {
            string newId = elementIdRemap[element.Id];
            MicrometreRect newBounds = new(
                new(element.Bounds.X.Value + offsetX),
                new(element.Bounds.Y.Value + offsetY),
                element.Bounds.Width,
                element.Bounds.Height);

            DocumentElement remapped = element switch
            {
                RectangleElement rect => rect with { Id = newId, Bounds = newBounds },
                LineElement line => RemapLine(line, newId, offsetX, offsetY),
                ImageElement img => img with
                {
                    Id = newId,
                    Bounds = newBounds,
                    AssetId = assetIdRemap.GetValueOrDefault(img.AssetId, img.AssetId),
                },
                TextElement text => text with { Id = newId, Bounds = newBounds },
                _ => element,
            };

            newElements.Add(remapped);
        }

        List<ElementGroup> newGroups = new();
        List<SelectionTarget> newTargets = new();

        foreach (ElementGroup group in payload.Groups)
        {
            string newGroupId = groupIdRemap[group.Id];
            List<string> remappedMembers = group.MemberIds
                .Select(m => elementIdRemap.GetValueOrDefault(m, m))
                .ToList();
            newGroups.Add(new ElementGroup(newGroupId, group.Name, remappedMembers, group.IsVisible, group.IsLocked));
            newTargets.Add(SelectionTarget.Group(newGroupId));
        }

        foreach (DocumentElement element in payload.Elements)
        {
            string newId = elementIdRemap[element.Id];
            bool isGroupMember = payload.Groups.Any(g => g.MemberIds.Contains(element.Id, StringComparer.Ordinal));
            if (!isGroupMember)
            {
                newTargets.Add(SelectionTarget.Element(newId));
            }
        }

        return new PastePlan
        {
            NewElements = newElements.AsReadOnly(),
            NewGroups = newGroups.AsReadOnly(),
            AssetRemap = assetIdRemap,
            ImportedAssets = importedAssets.AsReadOnly(),
            NewSelectionTargets = newTargets.AsReadOnly(),
        };
    }

    public void IncrementPasteCount() => _pasteCount++;

    private static LineElement RemapLine(LineElement line, string newId, int offsetX, int offsetY)
    {
        MicrometrePoint newStart = new(new(line.Start.X.Value + offsetX), new(line.Start.Y.Value + offsetY));
        MicrometrePoint newEnd = new(new(line.End.X.Value + offsetX), new(line.End.Y.Value + offsetY));
        return new LineElement(newId, newStart, newEnd, line.Thickness, line.Ink)
        {
            Name = line.Name,
            IsVisible = line.IsVisible,
            IsLocked = line.IsLocked,
            RotationMillidegrees = line.RotationMillidegrees,
        };
    }

    private static string ComputeHash(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}