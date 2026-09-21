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

    public LabelDocument Execute(LabelDocument document)
    {
        if (document.Elements.Any(element => element.Id == _element.Id))
        {
            throw new InvalidOperationException($"An element with ID '{_element.Id}' already exists.");
        }

        return document.WithElements([.. document.Elements, _element]);
    }

    public LabelDocument Undo(LabelDocument document)
    {
        ReplaceElementsCommand.EnsureUniqueTargets(document, [_element.Id]);
        return document.WithElements(document.Elements.Where(e => e.Id != _element.Id).ToList());
    }
}
