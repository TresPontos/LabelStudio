using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class MoveElementsCommand : IEditorCommand
{
    private readonly IReadOnlyDictionary<string, (MicrometreRect OldBounds, MicrometreRect NewBounds)> _changes;

    public MoveElementsCommand(IReadOnlyDictionary<string, (MicrometreRect OldBounds, MicrometreRect NewBounds)> changes)
    {
        _changes = changes;
    }

    public string Description => $"Move {_changes.Count} element(s)";

    public LabelDocument Execute(LabelDocument document) =>
        document.WithElements(document.Elements.Select(ApplyNewBounds).ToList());

    public LabelDocument Undo(LabelDocument document) =>
        document.WithElements(document.Elements.Select(ApplyOldBounds).ToList());

    private DocumentElement ApplyNewBounds(DocumentElement element) =>
        _changes.TryGetValue(element.Id, out var change)
            ? ElementFactory.WithBounds(element, change.NewBounds)
            : element;

    private DocumentElement ApplyOldBounds(DocumentElement element) =>
        _changes.TryGetValue(element.Id, out var change)
            ? ElementFactory.WithBounds(element, change.OldBounds)
            : element;
}