using LabelStudio.Document;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor;

public sealed class DocumentSession
{
    private LabelDocument _document;
    private string? _filePath;

    public LabelDocument Document => _document;
    public string? FilePath => _filePath;
    public CommandHistory History { get; } = new();

    public bool IsDirty => History.IsDirty;

    public event EventHandler? DocumentChanged;
    public event EventHandler? DirtyChanged;

    public DocumentSession(LabelDocument document)
    {
        _document = document;
    }

    public void SetDocument(LabelDocument document, string? filePath)
    {
        _document = document;
        _filePath = filePath;
        History.Clear();
        if (filePath is not null)
        {
            History.MarkSaved();
        }
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public LabelDocument ExecuteCommand(IEditorCommand command)
    {
        _document = History.Push(command, _document);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return _document;
    }

    public void Undo()
    {
        _document = History.Undo(_document);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        _document = History.Redo(_document);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved(string filePath)
    {
        _filePath = filePath;
        History.MarkSaved();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }
}