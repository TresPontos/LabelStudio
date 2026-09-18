using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class ResizeElementCommand : IEditorCommand
{
    private readonly string _elementId;
    private readonly MicrometreRect _oldBounds;
    private readonly MicrometreRect _newBounds;

    public ResizeElementCommand(string elementId, MicrometreRect oldBounds, MicrometreRect newBounds)
    {
        _elementId = elementId;
        _oldBounds = oldBounds;
        _newBounds = newBounds;
    }

    public string Description => $"Resize '{_elementId}'";

    public LabelDocument Execute(LabelDocument document) =>
        document.WithElements(document.Elements.Select(ApplyNew).ToList());

    public LabelDocument Undo(LabelDocument document) =>
        document.WithElements(document.Elements.Select(ApplyOld).ToList());

    private DocumentElement ApplyNew(DocumentElement element) =>
        element.Id == _elementId ? ElementFactory.WithBounds(element, _newBounds) : element;

    private DocumentElement ApplyOld(DocumentElement element) =>
        element.Id == _elementId ? ElementFactory.WithBounds(element, _oldBounds) : element;
}