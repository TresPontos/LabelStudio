using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class AddElementCommand : IEditorCommand
{
    private readonly DocumentElement _element;

    public AddElementCommand(DocumentElement element)
    {
        _element = element;
    }

    public string Description => $"Add {_element.ElementType} '{_element.Id}'";

    public LabelDocument Execute(LabelDocument document) =>
        document.WithElements([.. document.Elements, _element]);

    public LabelDocument Undo(LabelDocument document) =>
        document.WithElements(document.Elements.Where(e => e.Id != _element.Id).ToList());
}