using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class UngroupElementsCommand : IEditorCommand
{
    private readonly string _groupId;
    private readonly string[] _beforeElementOrder;
    private readonly ElementGroup _groupRecord;

    public string GroupId => _groupId;

    public UngroupElementsCommand(string groupId, LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException("Group ID cannot be null or empty.", nameof(groupId));
        }

        _groupId = groupId;
        _groupRecord = document.DesignMetadata.Groups
            .FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Group '{groupId}' does not exist.");

        _beforeElementOrder = document.Elements.Select(e => e.Id).ToArray();
    }

    public string Description => $"Ungroup '{_groupId}'";

    public LabelDocument Execute(LabelDocument document)
    {
        List<ElementGroup> groups = document.DesignMetadata.Groups
            .Where(g => !string.Equals(g.Id, _groupId, StringComparison.Ordinal))
            .ToList();

        DocumentDesignMetadata newMetadata = new(
            groups,
            document.DesignMetadata.Guides,
            document.DesignMetadata.Grid,
            document.DesignMetadata.SafeMargins);

        return document with { DesignMetadata = newMetadata };
    }

    public LabelDocument Undo(LabelDocument document)
    {
        List<ElementGroup> groups = [.. document.DesignMetadata.Groups, _groupRecord];
        DocumentDesignMetadata newMetadata = new(
            groups,
            document.DesignMetadata.Guides,
            document.DesignMetadata.Grid,
            document.DesignMetadata.SafeMargins);

        Dictionary<string, DocumentElement> byId = document.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        List<DocumentElement> elements = _beforeElementOrder
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();

        return document with
        {
            Elements = elements.AsReadOnly(),
            DesignMetadata = newMetadata,
        };
    }
}
