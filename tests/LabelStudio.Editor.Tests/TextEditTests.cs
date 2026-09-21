using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor.Tests;

public class TextEditTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62, 0);
        MediaSnapshot snap = new("test", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "test", snap, elements);
    }

    [Fact]
    public void ChangeText_ThenUndo_RestoresOriginalText()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();
        history.MarkSaved();

        doc = history.Push(new ChangePropertyCommand("t1", "text", "CAM", "CAMERA 001"), doc);

        Assert.Equal("CAMERA 001", doc.Elements[0] is TextElement t ? t.Text : "");

        doc = history.Undo(doc);
        Assert.Equal("CAM", ((TextElement)doc.Elements[0]).Text);
    }

    [Fact]
    public void ChangeText_ThenRedo_RestoresNewText()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "text", "CAM", "CAMERA 001"), doc);
        doc = history.Undo(doc);
        doc = history.Redo(doc);

        Assert.Equal("CAMERA 001", ((TextElement)doc.Elements[0]).Text);
    }

    [Fact]
    public void ChangeText_OneUndoEntry()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "text", "CAM", "CAMERA 001"), doc);

        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void ChangeFontSize_ThenUndo_RestoresOriginal()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Hello", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "fontSizePoints", 24, 36), doc);
        Assert.Equal(36, ((TextElement)doc.Elements[0]).FontSizePoints);

        doc = history.Undo(doc);
        Assert.Equal(24, ((TextElement)doc.Elements[0]).FontSizePoints);
    }

    [Fact]
    public void ChangeFontFamily_ThenUndo_RestoresOriginal()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Hello", 24, "Arial");
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "fontFamily", "Arial", "Consolas"), doc);
        Assert.Equal("Consolas", ((TextElement)doc.Elements[0]).FontFamily);

        doc = history.Undo(doc);
        Assert.Equal("Arial", ((TextElement)doc.Elements[0]).FontFamily);
    }

    [Fact]
    public void ChangeInk_ThenUndo_RestoresOriginal()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Hello", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "ink", InkChannel.Black, InkChannel.Red), doc);
        Assert.Equal(InkChannel.Red, ((TextElement)doc.Elements[0]).Ink);

        doc = history.Undo(doc);
        Assert.Equal(InkChannel.Black, ((TextElement)doc.Elements[0]).Ink);
    }

    [Fact]
    public void SameText_ProducesNoDocumentChange()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Same", 24, null);
        LabelDocument doc = CreateDoc(text);

        ChangePropertyCommand cmd = new("t1", "text", "Same", "Same");
        LabelDocument result = cmd.Execute(doc);

        Assert.Equal("Same", ((TextElement)result.Elements[0]).Text);
    }

    [Fact]
    public void Branch_UndoTextThenNewCommand_RemainsDirty()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "text", "CAM", "CAMERA"), doc);
        history.MarkSaved();
        doc = history.Undo(doc);

        doc = history.Push(new ChangePropertyCommand("t1", "text", "CAM", "CAMERA 001"), doc);

        Assert.True(history.IsDirty);
        Assert.False(history.CanRedo);
    }
}