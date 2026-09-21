using LabelStudio.Document;

namespace LabelStudio.Editor.Commands;

public sealed class CommandHistory
{
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private object _currentState = new();
    private object? _savedState;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool IsDirty => !ReferenceEquals(_currentState, _savedState);

    public event EventHandler? HistoryChanged;

    public LabelDocument Push(IEditorCommand command, LabelDocument document)
    {
        LabelDocument newDoc = command.Execute(document);
        object nextState = new();
        _undo.Push(new HistoryEntry(command, _currentState, nextState));
        _currentState = nextState;
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public LabelDocument Undo(LabelDocument document)
    {
        if (!CanUndo) return document;

        HistoryEntry entry = _undo.Pop();
        LabelDocument newDoc = entry.Command.Undo(document);
        _currentState = entry.BeforeState;
        _redo.Push(entry);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public LabelDocument Redo(LabelDocument document)
    {
        if (!CanRedo) return document;

        HistoryEntry entry = _redo.Pop();
        LabelDocument newDoc = entry.Command.Execute(document);
        _currentState = entry.AfterState;
        _undo.Push(entry);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public void MarkSaved()
    {
        _savedState = _currentState;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToClean()
    {
        _undo.Clear();
        _redo.Clear();
        _currentState = new object();
        _savedState = _currentState;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _currentState = new object();
        _savedState = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record HistoryEntry(IEditorCommand Command, object BeforeState, object AfterState);
}
