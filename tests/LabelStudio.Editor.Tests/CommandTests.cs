using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor.Tests;

public class CommandTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "brother.dk-22251", snap, elements);
    }

    [Fact]
    public void AddElement_ThenUndo_RemovesElement()
    {
        LabelDocument doc = CreateDoc();
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(1000), new(5000), new(3000)));
        LabelDocument withRect = new AddElementCommand(rect).Execute(doc);

        Assert.Single(withRect.Elements);

        LabelDocument undone = new AddElementCommand(rect).Undo(withRect);
        Assert.Empty(undone.Elements);
    }

    [Fact]
    public void DeleteElements_ThenUndo_RestoresElements()
    {
        RectangleElement r1 = RectangleElement.Create("r1", MicrometreRect.Zero);
        RectangleElement r2 = RectangleElement.Create("r2", MicrometreRect.Zero);
        LabelDocument doc = CreateDoc(r1, r2);

        DeleteElementsCommand cmd = new(["r1"], doc);
        LabelDocument deleted = cmd.Execute(doc);
        Assert.Single(deleted.Elements);

        LabelDocument undone = cmd.Undo(deleted);
        Assert.Equal(2, undone.Elements.Count);
    }

    [Fact]
    public void MoveElements_ThenUndo_RestoresExactBounds()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(2000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        var changes = new Dictionary<string, (MicrometreRect, MicrometreRect)>
        {
            ["r1"] = (rect.Bounds, new(new(3000), new(4000), new(5000), new(3000))),
        };

        MoveElementsCommand cmd = new(changes);
        LabelDocument moved = cmd.Execute(doc);
        Assert.Equal(3000, moved.Elements[0].Bounds.X.Value);
        Assert.Equal(4000, moved.Elements[0].Bounds.Y.Value);

        LabelDocument undone = cmd.Undo(moved);
        Assert.Equal(1000, undone.Elements[0].Bounds.X.Value);
        Assert.Equal(2000, undone.Elements[0].Bounds.Y.Value);
    }

    [Fact]
    public void ResizeElement_ThenRedo_RestoresExactBounds()
    {
        RectangleElement rect = RectangleElement.Create("r1", new(new(1000), new(2000), new(5000), new(3000)));
        LabelDocument doc = CreateDoc(rect);

        MicrometreRect newBounds = new(new(500), new(1000), new(8000), new(6000));
        ResizeElementCommand cmd = new("r1", rect.Bounds, newBounds);

        LabelDocument resized = cmd.Execute(doc);
        Assert.Equal(8000, resized.Elements[0].Bounds.Width.Value);
        Assert.Equal(6000, resized.Elements[0].Bounds.Height.Value);

        LabelDocument redone = cmd.Execute(cmd.Undo(resized));
        Assert.Equal(8000, redone.Elements[0].Bounds.Width.Value);
    }
}