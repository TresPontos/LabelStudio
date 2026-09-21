using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class PasteElementsCommand : IEditorCommand
{
    private readonly DocumentElement[] _newElements;
    private readonly ElementGroup[] _newGroups;
    private readonly DocumentDesignMetadata _beforeDesignMetadata;

    public IReadOnlyList<DocumentElement> NewElements => _newElements;
    public IReadOnlyList<ElementGroup> NewGroups => _newGroups;

    public PasteElementsCommand(
        IReadOnlyList<DocumentElement> newElements,
        IReadOnlyList<ElementGroup> newGroups,
        LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(newElements);
        ArgumentNullException.ThrowIfNull(newGroups);
        ArgumentNullException.ThrowIfNull(document);

        _newElements = newElements.ToArray();
        _newGroups = newGroups.ToArray();
        _beforeDesignMetadata = document.DesignMetadata;

        HashSet<string> existingIds = document.Elements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (DocumentElement element in _newElements)
        {
            if (existingIds.Contains(element.Id))
            {
                throw new InvalidOperationException($"Element with ID '{element.Id}' already exists in the document.");
            }
        }

        HashSet<string> existingGroupIds = document.DesignMetadata.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (ElementGroup group in _newGroups)
        {
            if (existingGroupIds.Contains(group.Id))
            {
                throw new InvalidOperationException($"Group with ID '{group.Id}' already exists in the document.");
            }
        }
    }

    public string Description => $"Paste {_newElements.Length} element(s)";

    public LabelDocument Execute(LabelDocument document)
    {
        List<DocumentElement> elements = [.. document.Elements, .. _newElements];
        List<ElementGroup> groups = [.. document.DesignMetadata.Groups, .. _newGroups];
        DocumentDesignMetadata metadata = new(groups, document.DesignMetadata.Guides, document.DesignMetadata.Grid);
        return document with { Elements = elements.AsReadOnly(), DesignMetadata = metadata };
    }

    public LabelDocument Undo(LabelDocument document)
    {
        HashSet<string> pastedIds = _newElements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        List<DocumentElement> elements = document.Elements.Where(e => !pastedIds.Contains(e.Id)).ToList();

        HashSet<string> pastedGroupIds = _newGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        List<ElementGroup> groups = document.DesignMetadata.Groups
            .Where(g => !pastedGroupIds.Contains(g.Id))
            .ToList();

        DocumentDesignMetadata metadata = new(groups, document.DesignMetadata.Guides, document.DesignMetadata.Grid);
        return document with { Elements = elements.AsReadOnly(), DesignMetadata = metadata };
    }
}