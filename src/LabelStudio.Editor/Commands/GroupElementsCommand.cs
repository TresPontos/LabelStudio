using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class GroupElementsCommand : IEditorCommand
{
    private readonly string _groupId;
    private readonly string? _groupName;
    private readonly string[] _memberIds;
    private readonly string[] _beforeElementOrder;

    public string GroupId => _groupId;

    public GroupElementsCommand(IEnumerable<string> elementIds, LabelDocument document, string? groupName = null)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        ArgumentNullException.ThrowIfNull(document);

        HashSet<string> existingIds = document.Elements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        _memberIds = elementIds.Where(existingIds.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (_memberIds.Length < 2)
        {
            throw new ArgumentException("At least two elements are required to form a group.", nameof(elementIds));
        }

        _beforeElementOrder = document.Elements.Select(e => e.Id).ToArray();
        if (_beforeElementOrder.Distinct(StringComparer.Ordinal).Count() != _beforeElementOrder.Length)
        {
            throw new InvalidOperationException("Document contains duplicate element IDs.");
        }

        foreach (string memberId in _memberIds)
        {
            ElementGroup? existingGroup = document.DesignMetadata.Groups
                .FirstOrDefault(g => g.MemberIds.Contains(memberId, StringComparer.Ordinal));
            if (existingGroup is not null)
            {
                throw new InvalidOperationException(
                    $"Element '{memberId}' already belongs to group '{existingGroup.Id}'. Nested groups are not supported.");
            }
        }

        _groupId = Guid.NewGuid().ToString("D");
        _groupName = groupName;
    }

    public string Description => $"Group {_memberIds.Length} elements";

    public LabelDocument Execute(LabelDocument document)
    {
        if (!_beforeElementOrder.SequenceEqual(document.Elements.Select(e => e.Id), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Document element order has changed since the group command was created.");
        }

        HashSet<string> memberSet = _memberIds.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, DocumentElement> byId = document.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);

        int topmostMemberIndex = -1;
        for (int i = 0; i < document.Elements.Count; i++)
        {
            if (memberSet.Contains(document.Elements[i].Id))
            {
                topmostMemberIndex = i;
            }
        }

        List<DocumentElement> before = [];
        List<DocumentElement> after = [];
        for (int i = 0; i < document.Elements.Count; i++)
        {
            if (memberSet.Contains(document.Elements[i].Id)) continue;
            if (i < topmostMemberIndex)
            {
                before.Add(document.Elements[i]);
            }
            else
            {
                after.Add(document.Elements[i]);
            }
        }

        List<DocumentElement> members = _memberIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        List<DocumentElement> reordered = [.. before, .. members, .. after];

        ElementGroup newGroup = new(_groupId, _groupName, _memberIds);
        List<ElementGroup> groups = [.. document.DesignMetadata.Groups, newGroup];
        DocumentDesignMetadata newMetadata = new(groups, document.DesignMetadata.Guides, document.DesignMetadata.Grid);

        return document with
        {
            Elements = reordered.AsReadOnly(),
            DesignMetadata = newMetadata,
        };
    }

    public LabelDocument Undo(LabelDocument document)
    {
        Dictionary<string, DocumentElement> byId = document.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);

        List<ElementGroup> groups = document.DesignMetadata.Groups
            .Where(g => !string.Equals(g.Id, _groupId, StringComparison.Ordinal))
            .ToList();

        DocumentDesignMetadata newMetadata = new(groups, document.DesignMetadata.Guides, document.DesignMetadata.Grid);

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