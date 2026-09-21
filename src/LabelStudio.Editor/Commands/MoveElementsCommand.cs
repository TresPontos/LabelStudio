using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class MoveElementsCommand : IEditorCommand
{
    private readonly Dictionary<string, (MicrometreRect OldBounds, MicrometreRect NewBounds)> _changes;
    private ReplaceElementsCommand? _replacement;

    public MoveElementsCommand(IReadOnlyDictionary<string, (MicrometreRect OldBounds, MicrometreRect NewBounds)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = changes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public string Description => $"Move {_changes.Count} element(s)";

    public LabelDocument Execute(LabelDocument document)
    {
        _replacement ??= BuildReplacement(document);
        return _replacement.Execute(document);
    }

    public LabelDocument Undo(LabelDocument document) =>
        (_replacement ?? throw new InvalidOperationException("The command has not been executed.")).Undo(document);

    private ReplaceElementsCommand BuildReplacement(LabelDocument document)
    {
        ReplaceElementsCommand.EnsureUniqueTargets(document, _changes.Keys);
        var replacements = new List<(DocumentElement Before, DocumentElement After)>();
        foreach (DocumentElement element in document.Elements)
        {
            if (_changes.TryGetValue(element.Id, out var change))
            {
                replacements.Add((element, ElementFactory.WithBounds(element, change.NewBounds)));
            }
        }

        return new ReplaceElementsCommand(replacements);
    }
}
