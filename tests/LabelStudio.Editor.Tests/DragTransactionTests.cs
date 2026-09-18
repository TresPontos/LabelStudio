using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor.Tests;

public class DragTransactionTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62, 0);
        MediaSnapshot snap = new("test", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "test", snap, elements);
    }

    [Fact]
    public void Drag_ProducesOneUndoEntry()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);
        CommandHistory history = new();
        doc = history.Push(new AddElementCommand(rect), doc);
        history.MarkSaved();

        DragTransaction drag = new();
        drag.Begin(["r1"], doc);

        drag.UpdatePreview("r1", new(new(2000), new(1000), new(5000), new(3000)));
        drag.UpdatePreview("r1", new(new(3000), new(1000), new(5000), new(3000)));
        drag.UpdatePreview("r1", new(new(4000), new(1000), new(5000), new(3000)));

        IEditorCommand? cmd = drag.Commit(doc);
        Assert.NotNull(cmd);
        doc = history.Push(cmd!, doc);

        Assert.Equal(2, history.UndoCount);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Drag_NoChange_ProducesNullCommand()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        DragTransaction drag = new();
        drag.Begin(["r1"], doc);
        drag.UpdatePreview("r1", rect.Bounds);

        IEditorCommand? cmd = drag.Commit(doc);
        Assert.Null(cmd);
    }

    [Fact]
    public void Drag_Cancel_ProducesNoCommand()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        DragTransaction drag = new();
        drag.Begin(["r1"], doc);
        drag.UpdatePreview("r1", new(new(5000), new(5000), new(5000), new(3000)));
        drag.Cancel();

        Assert.False(drag.IsActive);
    }

    [Fact]
    public void Redo_RestoresExactGeometry()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1234), new(5678), new(4321), new(8765)));
        LabelDocument doc = CreateDoc(rect);
        CommandHistory history = new();
        doc = history.Push(new AddElementCommand(rect), doc);
        history.MarkSaved();

        MicrometreRect original = doc.Elements[0].Bounds;
        MicrometreRect moved = new(new(9999), new(1111), new(4321), new(8765));

        var changes = new Dictionary<string, (MicrometreRect, MicrometreRect)>
        {
            ["r1"] = (original, moved),
        };

        doc = history.Push(new MoveElementsCommand(changes), doc);
        Assert.Equal(9999, doc.Elements[0].Bounds.X.Value);

        doc = history.Undo(doc);
        Assert.Equal(original, doc.Elements[0].Bounds);

        doc = history.Redo(doc);
        Assert.Equal(9999, doc.Elements[0].Bounds.X.Value);
        Assert.Equal(1111, doc.Elements[0].Bounds.Y.Value);
    }
}