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
    public void ChangeRotation_ThenUndo_RestoresOriginalRotation()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Same", 24, null);
        LabelDocument doc = CreateDoc(text);
        CommandHistory history = new();

        doc = history.Push(new ChangePropertyCommand("t1", "rotationMillidegrees", 0, 90000), doc);
        Assert.Equal(90000, ((TextElement)doc.Elements[0]).RotationMillidegrees);

        doc = history.Undo(doc);
        Assert.Equal(0, ((TextElement)doc.Elements[0]).RotationMillidegrees);
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

    [Fact]
    public void Session_UpdateChangesDocumentLiveWithoutCreatingHistory()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        documentSession.History.MarkSaved();
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        edit.Update("CAMERA");

        Assert.Equal("CAMERA", ((TextElement)documentSession.Document.Elements[0]).Text);
        Assert.Equal("CAMERA", edit.CurrentText);
        Assert.Equal(0, documentSession.History.UndoCount);
        Assert.True(documentSession.IsDirty);
    }

    [Fact]
    public void Session_ManyUpdatesCommitAsOneUndoableOperation()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "CAM", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        foreach (string value in new[] { "CAME", "CAMER", "CAMERA", "CAMERA 001" })
        {
            edit.Update(value);
        }
        edit.Commit();

        Assert.Equal(1, documentSession.History.UndoCount);
        Assert.Equal("CAMERA 001", ((TextElement)documentSession.Document.Elements[0]).Text);

        documentSession.Undo();
        Assert.Equal("CAM", ((TextElement)documentSession.Document.Elements[0]).Text);

        documentSession.Redo();
        Assert.Equal("CAMERA 001", ((TextElement)documentSession.Document.Elements[0]).Text);
    }

    [Fact]
    public void Session_CancelRestoresOriginalAndCreatesNoCommand()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Original", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        documentSession.History.MarkSaved();
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        edit.Update("Changed");
        edit.Cancel();

        Assert.Equal("Original", ((TextElement)documentSession.Document.Elements[0]).Text);
        Assert.Equal(0, documentSession.History.UndoCount);
        Assert.False(edit.IsActive);
        Assert.False(documentSession.IsDirty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Line one\nLine two")]
    public void Session_AllowsEmptyAndMultilineText(string value)
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Original", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        edit.Update(value);
        edit.Commit();

        Assert.Equal(value, ((TextElement)documentSession.Document.Elements[0]).Text);
    }

    [Fact]
    public void Session_UndoBackToSavePointClearsDirtyState()
    {
        TextElement text = new("t1", new(new(1000), new(1000), new(20000), new(3000)),
            InkChannel.Black, "Original", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        documentSession.History.MarkSaved();
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        edit.Update("Changed");
        edit.Commit();
        Assert.True(documentSession.IsDirty);

        documentSession.Undo();
        Assert.False(documentSession.IsDirty);
        Assert.Equal("Original", ((TextElement)documentSession.Document.Elements[0]).Text);
    }

    [Fact]
    public void Session_FixedFrameTyping_DoesNotChangeBounds()
    {
        TextElement text = new("t1", new(new(1000), new(2000), new(9000), new(3000)),
            InkChannel.Black, "Short", 24, null);
        DocumentSession documentSession = new(CreateDoc(text));
        TextEditSession edit = new(documentSession);

        edit.Begin("t1");
        edit.Update("A much longer value that exceeds the fixed frame");
        edit.Commit();

        Assert.Equal(text.Bounds, ((TextElement)documentSession.Document.Elements[0]).Bounds);
    }

    [Fact]
    public void Session_AutomaticFrameChange_UndoRestoresTextAndBoundsTogether()
    {
        TextElement text = new("t1", new(new(1000), new(2000), new(9000), new(3000)),
            InkChannel.Black, "Short", 24, null)
        {
            FrameSizing = TextFrameSizingMode.AutoHeight,
        };
        DocumentSession documentSession = new(CreateDoc(text));
        TextEditSession edit = new(documentSession, element => element with
        {
            Bounds = new MicrometreRect(
                element.Bounds.X,
                element.Bounds.Y,
                element.Bounds.Width,
                new Micrometre(element.Text.Length * 100)),
        });

        edit.Begin("t1");
        edit.Update("Longer text");
        edit.Commit();

        TextElement changed = (TextElement)documentSession.Document.Elements[0];
        Assert.Equal("Longer text", changed.Text);
        Assert.Equal(1100, changed.Bounds.Height.Value);
        Assert.Equal(1, documentSession.History.UndoCount);

        documentSession.Undo();
        TextElement restored = (TextElement)documentSession.Document.Elements[0];
        Assert.Equal(text.Text, restored.Text);
        Assert.Equal(text.Bounds, restored.Bounds);
    }
}
