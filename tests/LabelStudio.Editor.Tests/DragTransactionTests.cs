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
        LabelDocument doc = CreateDoc();
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
    public void Resize_AutomaticTextFrame_BecomesFixedAndUndoesAtomically()
    {
        TextElement text = new(
            "t1",
            new MicrometreRect(new(1000), new(1000), new(5000), new(3000)),
            InkChannel.Black,
            "Text",
            18,
            null)
        {
            FrameSizing = TextFrameSizingMode.AutoHeight,
        };
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();
        DragTransaction drag = new();
        drag.Begin([text.Id], doc);
        MicrometreRect resized = new(new(1000), new(1000), new(8000), new(4000));

        IEditorCommand? command = drag.Commit(
            doc,
            new Dictionary<string, MicrometreRect> { [text.Id] = resized },
            setTextFrameFixed: true);
        doc = history.Push(Assert.IsAssignableFrom<IEditorCommand>(command), doc);

        TextElement changed = (TextElement)doc.Elements[0];
        Assert.Equal(resized, changed.Bounds);
        Assert.Equal(TextFrameSizingMode.Fixed, changed.FrameSizing);
        Assert.Equal(1, history.UndoCount);

        doc = history.Undo(doc);
        Assert.Equal(text, doc.Elements[0]);
    }

    [Fact]
    public void Redo_RestoresExactGeometry()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1234), new(5678), new(4321), new(8765)));
        LabelDocument doc = CreateDoc();
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
