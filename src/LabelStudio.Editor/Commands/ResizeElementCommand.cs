using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class ResizeElementCommand : IEditorCommand
{
    private readonly string _elementId;
    private readonly MicrometreRect _oldBounds;
    private readonly MicrometreRect _newBounds;
    private ReplaceElementsCommand? _replacement;

    public ResizeElementCommand(string elementId, MicrometreRect oldBounds, MicrometreRect newBounds)
    {
        _elementId = elementId;
        _oldBounds = oldBounds;
        _newBounds = newBounds;
    }

    public string Description => $"Resize '{_elementId}'";

    public LabelDocument Execute(LabelDocument document)
    {
        _replacement ??= BuildReplacement(document);
        return _replacement.Execute(document);
    }

    public LabelDocument Undo(LabelDocument document) =>
        (_replacement ?? throw new InvalidOperationException("The command has not been executed.")).Undo(document);

    private ReplaceElementsCommand BuildReplacement(LabelDocument document)
    {
        ReplaceElementsCommand.EnsureUniqueTargets(document, [_elementId]);
        DocumentElement? before = document.Elements.FirstOrDefault(element => element.Id == _elementId);
        if (before is null) return new ReplaceElementsCommand([]);

        DocumentElement resized = ElementFactory.WithBounds(before, _newBounds);
        if (resized is TextElement text)
        {
            resized = text with { FrameSizing = TextFrameSizingMode.Fixed };
        }
        return new ReplaceElementsCommand([(before, resized)]);
    }
}
