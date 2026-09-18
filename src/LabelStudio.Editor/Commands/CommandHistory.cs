using LabelStudio.Document;

namespace LabelStudio.Editor.Commands;

public sealed class CommandHistory
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();
    private int _savedUndoDepth = -1;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private int _epoch;
    private int _savedEpochNumber;

    public bool IsDirty
    {
        get
        {
            if (_savedUndoDepth < 0) return true;
            if (_epoch != _savedEpochNumber) return true;
            return _undo.Count != _savedUndoDepth;
        }
    }

    public event EventHandler? HistoryChanged;

    public CommandHistory()
    {
        _epoch = 0;
        _savedUndoDepth = -1;
        _savedEpochNumber = -1;
    }

    public LabelDocument Push(IEditorCommand command, LabelDocument document)
    {
        LabelDocument newDoc = command.Execute(document);
        _undo.Push(command);
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public LabelDocument Undo(LabelDocument document)
    {
        if (!CanUndo) return document;

        IEditorCommand command = _undo.Pop();
        LabelDocument newDoc = command.Undo(document);
        _redo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public LabelDocument Redo(LabelDocument document)
    {
        if (!CanRedo) return document;

        IEditorCommand command = _redo.Pop();
        LabelDocument newDoc = command.Execute(document);
        _undo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return newDoc;
    }

    public void MarkSaved()
    {
        _savedUndoDepth = _undo.Count;
        _savedEpochNumber = _epoch;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToClean()
    {
        _undo.Clear();
        _redo.Clear();
        _savedUndoDepth = 0;
        _epoch = 0;
        _savedEpochNumber = 0;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _epoch++;
        _savedUndoDepth = -1;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}