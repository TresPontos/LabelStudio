using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor.Tests;

public class CommandHistoryTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "brother.dk-22251", snap, elements);
    }

    [Fact]
    public void Push_IncreasesRevisionAndSetsDirty()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        RectangleElement rect = RectangleElement.Create("r1", MicrometreRect.Zero);

        Assert.True(history.IsDirty);

        history.MarkSaved();
        Assert.False(history.IsDirty);

        doc = history.Push(new AddElementCommand(rect), doc);
        Assert.True(history.IsDirty);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_RestoresDocumentAndClearsDirty_WhenAtSavedRevision()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        RectangleElement rect = RectangleElement.Create("r1", MicrometreRect.Zero);

        doc = history.Push(new AddElementCommand(rect), doc);
        history.MarkSaved();
        Assert.False(history.IsDirty);

        doc = history.Push(new DeleteElementsCommand(["r1"], doc), doc);
        Assert.True(history.IsDirty);

        doc = history.Undo(doc);
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Redo_AfterUndo_RestoresStateAndSetsDirty()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        RectangleElement rect = RectangleElement.Create("r1", MicrometreRect.Zero);

        doc = history.Push(new AddElementCommand(rect), doc);
        history.MarkSaved();
        doc = history.Undo(doc);
        Assert.True(history.IsDirty);

        doc = history.Redo(doc);
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Branch_UndoThenNewCommand_ClearsRedoAndCreatesNewIdentity()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        RectangleElement r1 = RectangleElement.Create("r1", MicrometreRect.Zero);
        RectangleElement r2 = RectangleElement.Create("r2", MicrometreRect.Zero);

        doc = history.Push(new AddElementCommand(r1), doc);
        history.MarkSaved();
        int savedUndoCount = history.UndoCount;

        doc = history.Push(new AddElementCommand(r2), doc);
        doc = history.Undo(doc);

        Assert.False(history.IsDirty);

        RectangleElement r3 = RectangleElement.Create("r3", MicrometreRect.Zero);
        doc = history.Push(new AddElementCommand(r3), doc);

        Assert.True(history.IsDirty);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void EqualDepthBranch_DoesNotCollideWithSavedStateIdentity()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        doc = history.Push(new AddElementCommand(RectangleElement.Create("r1", MicrometreRect.Zero)), doc);
        doc = history.Push(new AddElementCommand(RectangleElement.Create("saved", MicrometreRect.Zero)), doc);
        history.MarkSaved();

        doc = history.Undo(doc);
        doc = history.Push(new AddElementCommand(RectangleElement.Create("branch", MicrometreRect.Zero)), doc);

        Assert.Equal(2, history.UndoCount);
        Assert.True(history.IsDirty);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RepeatedUndoRedo_NoDrift()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        RectangleElement rect = RectangleElement.Create("r1", new(new(1234), new(5678), new(4321), new(8765)));

        doc = history.Push(new AddElementCommand(rect), doc);

        MicrometreRect originalBounds = doc.Elements[0].Bounds;

        for (int i = 0; i < 10; i++)
        {
            doc = history.Undo(doc);
            doc = history.Redo(doc);
        }

        Assert.Equal(originalBounds, doc.Elements[0].Bounds);
    }

    [Fact]
    public void Clear_ResetsToClean()
    {
        CommandHistory history = new();
        LabelDocument doc = CreateDoc();
        doc = history.Push(new AddElementCommand(RectangleElement.Create("r1", MicrometreRect.Zero)), doc);

        history.ResetToClean();
        Assert.False(history.IsDirty);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}
