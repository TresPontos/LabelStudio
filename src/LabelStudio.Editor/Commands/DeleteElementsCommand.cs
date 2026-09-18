using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class DeleteElementsCommand : IEditorCommand
{
    private readonly IReadOnlyList<DocumentElement> _deletedElements;
    private readonly HashSet<string> _idsToDelete;

    public DeleteElementsCommand(IReadOnlyList<string> elementIds, LabelDocument document)
    {
        _idsToDelete = elementIds.ToHashSet();
        _deletedElements = document.Elements.Where(e => _idsToDelete.Contains(e.Id)).ToList();
    }

    public string Description => $"Delete {_deletedElements.Count} element(s)";

    public LabelDocument Execute(LabelDocument document) =>
        document.WithElements(document.Elements.Where(e => !_idsToDelete.Contains(e.Id)).ToList());

    public LabelDocument Undo(LabelDocument document) =>
        document.WithElements([.. document.Elements, .. _deletedElements]);
}