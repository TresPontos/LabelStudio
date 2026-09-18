using LabelStudio.Document;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public interface IEditorCommand
{
    string Description { get; }
    LabelDocument Execute(LabelDocument document);
    LabelDocument Undo(LabelDocument document);
}