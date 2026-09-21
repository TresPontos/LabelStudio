using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class DeleteElementsCommand : IEditorCommand
{
    private readonly DeletedElement[] _deletedElements;
    private readonly HashSet<string> _idsToDelete;

    public DeleteElementsCommand(IReadOnlyList<string> elementIds, LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        _idsToDelete = elementIds.ToHashSet(StringComparer.Ordinal);
        ReplaceElementsCommand.EnsureUniqueTargets(document, _idsToDelete);
        _deletedElements = document.Elements
            .Select((element, index) => new DeletedElement(index, element))
            .Where(entry => _idsToDelete.Contains(entry.Element.Id))
            .ToArray();
    }

    public string Description => $"Delete {_deletedElements.Length} element(s)";

    public LabelDocument Execute(LabelDocument document)
    {
        ReplaceElementsCommand.EnsureUniqueTargets(document, _idsToDelete);
        return document.WithElements(document.Elements.Where(e => !_idsToDelete.Contains(e.Id)).ToList());
    }

    public LabelDocument Undo(LabelDocument document)
    {
        ReplaceElementsCommand.EnsureUniqueTargets(document, _idsToDelete);
        string? existingId = document.Elements.FirstOrDefault(element => _idsToDelete.Contains(element.Id))?.Id;
        if (existingId is not null)
        {
            throw new InvalidOperationException($"An element with ID '{existingId}' already exists.");
        }

        List<DocumentElement> elements = document.Elements.ToList();
        foreach (DeletedElement deleted in _deletedElements)
        {
            elements.Insert(Math.Min(deleted.Index, elements.Count), deleted.Element);
        }

        return document.WithElements(elements.AsReadOnly());
    }

    private sealed record DeletedElement(int Index, DocumentElement Element);
}
