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
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 48.26);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        return LabelDocument.Create(
            dims, "brother.dk-22251", snap, elements, mediaKind: DocumentMediaKind.Continuous);
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
    public void DeleteElements_UndoRestoresExactOriginalOrder()
    {
        RectangleElement[] elements = new[] { "a", "b", "c", "d", "e" }
            .Select(id => RectangleElement.Create(id, MicrometreRect.Zero))
            .ToArray();
        LabelDocument doc = CreateDoc(elements);

        DeleteElementsCommand cmd = new(["b", "d"], doc);
        LabelDocument restored = cmd.Undo(cmd.Execute(doc));

        Assert.Equal(["a", "b", "c", "d", "e"], restored.Elements.Select(element => element.Id));
        Assert.True(elements.Cast<DocumentElement>().SequenceEqual(restored.Elements));
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

    [Fact]
    public void MoveDiagonalLine_UpdatesEndpointsAndUndoRedoUsesExactRecords()
    {
        LineElement line = new(
            "line",
            new MicrometrePoint(new(100), new(500)),
            new MicrometrePoint(new(1100), new(100)),
            new Micrometre(20),
            InkChannel.Red)
        {
            Name = "diagonal",
            IsLocked = true,
            RotationMillidegrees = 12_000,
        };
        LabelDocument doc = CreateDoc(line);
        MicrometreRect movedBounds = line.Bounds.Offset(new Micrometre(300), new Micrometre(-200));
        MoveElementsCommand command = new(new Dictionary<string, (MicrometreRect, MicrometreRect)>
        {
            [line.Id] = (line.Bounds, movedBounds),
        });

        LabelDocument movedDocument = command.Execute(doc);
        LineElement moved = Assert.IsType<LineElement>(Assert.Single(movedDocument.Elements));
        Assert.Equal(line.Start.Offset(new Micrometre(300), new Micrometre(-200)), moved.Start);
        Assert.Equal(line.End.Offset(new Micrometre(300), new Micrometre(-200)), moved.End);
        Assert.Equal(movedBounds, moved.Bounds);
        Assert.Equal(line.Name, moved.Name);
        Assert.Equal(line.IsLocked, moved.IsLocked);
        Assert.Equal(line.RotationMillidegrees, moved.RotationMillidegrees);

        Assert.Same(line, Assert.Single(command.Undo(movedDocument).Elements));
        Assert.Equal(moved, Assert.Single(command.Execute(doc).Elements));
    }

    [Fact]
    public void ResizeDiagonalLine_UpdatesEndpointsAndComputedBounds()
    {
        LineElement line = new(
            "line",
            new MicrometrePoint(new(100), new(500)),
            new MicrometrePoint(new(1100), new(100)),
            new Micrometre(20),
            InkChannel.Black);
        LabelDocument doc = CreateDoc(line);
        MicrometreRect resizedBounds = new(new(200), new(300), new(2020), new(820));
        ResizeElementCommand command = new(line.Id, line.Bounds, resizedBounds);

        LineElement resized = Assert.IsType<LineElement>(Assert.Single(command.Execute(doc).Elements));

        Assert.Equal(new MicrometrePoint(new(210), new(1110)), resized.Start);
        Assert.Equal(new MicrometrePoint(new(2210), new(310)), resized.End);
        Assert.Equal(resizedBounds, resized.Bounds);
        Assert.Same(line, Assert.Single(command.Undo(CreateDoc(resized)).Elements));
    }

    [Fact]
    public void Commands_DefensivelyCopyMutablePayloads()
    {
        RectangleElement before = RectangleElement.Create("r1", new(new(0), new(0), new(100), new(100)));
        RectangleElement after = before with { Bounds = new(new(50), new(50), new(100), new(100)) };
        var replacements = new List<(DocumentElement Before, DocumentElement After)> { (before, after) };
        ReplaceElementsCommand replace = new(replacements);
        replacements.Clear();

        var changes = new Dictionary<string, (MicrometreRect, MicrometreRect)>
        {
            ["r1"] = (before.Bounds, after.Bounds),
        };
        MoveElementsCommand move = new(changes);
        changes.Clear();

        List<string> ids = ["r1"];
        DeleteElementsCommand delete = new(ids, CreateDoc(before));
        ids.Clear();

        Assert.Equal(after, Assert.Single(replace.Execute(CreateDoc(before)).Elements));
        Assert.Equal(after.Bounds, Assert.Single(move.Execute(CreateDoc(before)).Elements).Bounds);
        Assert.Empty(delete.Execute(CreateDoc(before)).Elements);
    }

    [Fact]
    public void AddElement_RejectsDuplicateIdAtExecuteTime()
    {
        RectangleElement existing = RectangleElement.Create("duplicate", MicrometreRect.Zero);
        AddElementCommand command = new(RectangleElement.Create("duplicate", MicrometreRect.Zero));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => command.Execute(CreateDoc(existing)));

        Assert.Contains("duplicate", error.Message);
    }

    [Fact]
    public void ChangeLineThickness_RecomputesBounds()
    {
        LineElement line = new(
            "line",
            new MicrometrePoint(new(1000), new(2000)),
            new MicrometrePoint(new(5000), new(2000)),
            new Micrometre(100),
            InkChannel.Black);
        LabelDocument document = CreateDoc(line);

        LabelDocument changed = new ChangePropertyCommand(
            line.Id,
            "thickness",
            line.Thickness,
            new Micrometre(400)).Execute(document);

        LineElement result = Assert.IsType<LineElement>(changed.Elements[0]);
        Assert.Equal(400, result.Thickness.Value);
        Assert.Equal(800, result.Bounds.X.Value);
        Assert.Equal(1800, result.Bounds.Y.Value);
        Assert.Equal(4400, result.Bounds.Width.Value);
        Assert.Equal(400, result.Bounds.Height.Value);
    }

    [Fact]
    public void ChangeMedia_UpdatesProfileAndGeometryAndUndoRestoresThem()
    {
        LabelDocument document = CreateDoc(RectangleElement.Create("r1", MicrometreRect.Zero));
        PhysicalSize newDimensions = PhysicalSize.FromMillimetres(17, 53.9);
        MediaSnapshot newGeometry = new(
            "brother.dk-11204",
            newDimensions,
            new(new(1500), new(3000), new(14_000), new(47_900)));
        ChangeMediaCommand command = new(
            document, "brother.dk-11204", newDimensions, newGeometry, DocumentMediaKind.DieCut);

        LabelDocument changed = command.Execute(document);

        Assert.Equal("brother.dk-11204", changed.MediaProfileId);
        Assert.Equal(newDimensions, changed.PageDimensions);
        Assert.Equal(DocumentMediaKind.DieCut, changed.MediaKind);
        Assert.Equal(newGeometry, changed.MediaGeometry);
        Assert.Same(document.Elements[0], changed.Elements[0]);
        Assert.Equal(document, command.Undo(changed));
    }

    [Fact]
    public void ChangePageLength_UpdatesOnlyLengthAndIsUndoable()
    {
        LabelDocument document = CreateDoc();
        ChangePageLengthCommand command = new(document, new Micrometre(100_000));

        LabelDocument changed = command.Execute(document);

        Assert.Equal(document.PageDimensions.Width, changed.PageDimensions.Width);
        Assert.Equal(new Micrometre(100_000), changed.PageDimensions.Height);
        Assert.Equal(new Micrometre(100_000), changed.MediaGeometry.PhysicalDimensions.Height);
        Assert.Equal(document, command.Undo(changed));
    }

    [Fact]
    public void ChangePageLength_DoesNotMoveContentBeyondNewBoundary()
    {
        RectangleElement element = RectangleElement.Create(
            "outside",
            new(new(5_000), new(40_000), new(10_000), new(5_000)));
        LabelDocument document = CreateDoc(element);
        ChangePageLengthCommand command = new(document, new Micrometre(30_000));

        LabelDocument changed = command.Execute(document);

        Assert.Same(element, changed.Elements[0]);
        Assert.Equal(new Micrometre(40_000), changed.Elements[0].Bounds.Y);
        Assert.True(changed.Elements[0].Bounds.Bottom > changed.PageDimensions.Height);
    }

    [Fact]
    public void ChangePageLength_RejectsWidthChangeAndDieCutMedia()
    {
        LabelDocument continuous = CreateDoc();
        Assert.Throws<ArgumentException>(() => new ChangePageLengthCommand(
            continuous,
            new PhysicalSize(new Micrometre(61_000), new Micrometre(100_000))));

        LabelDocument dieCut = continuous with { MediaKind = DocumentMediaKind.DieCut };
        Assert.Throws<InvalidOperationException>(() =>
            new ChangePageLengthCommand(dieCut, new Micrometre(100_000)));
    }

    [Fact]
    public void ChangeElementMetadata_UsesExactTypedBeforeAndAfterRecords()
    {
        RectangleElement beforeElement = RectangleElement.Create("r1", MicrometreRect.Zero) with
        {
            Name = "Before",
            IsVisible = true,
            IsLocked = false,
        };
        ElementMetadata before = ElementMetadata.From(beforeElement);
        ElementMetadata after = new("After", false, true);
        ChangeElementMetadataCommand command = new(new ElementMetadataChange("r1", before, after));

        DocumentElement changed = Assert.Single(command.Execute(CreateDoc(beforeElement)).Elements);
        Assert.Equal("After", changed.Name);
        Assert.False(changed.IsVisible);
        Assert.True(changed.IsLocked);

        DocumentElement restored = Assert.Single(command.Undo(CreateDoc(changed)).Elements);
        Assert.Equal(before, ElementMetadata.From(restored));
        Assert.Equal(after, ElementMetadata.From(Assert.Single(command.Execute(CreateDoc(restored)).Elements)));
    }

    [Theory]
    [InlineData("forward", "a,c,b,e,d")]
    [InlineData("backward", "b,a,d,c,e")]
    [InlineData("front", "a,c,e,b,d")]
    [InlineData("back", "b,d,a,c,e")]
    public void ZOrderCommands_PreserveMultiSelectionOrderAndUndoExactly(string operation, string expected)
    {
        RectangleElement[] elements = [.. new[] { "a", "b", "c", "d", "e" }
            .Select(id => RectangleElement.Create(id, MicrometreRect.Zero))];
        LabelDocument document = CreateDoc(elements);
        IEditorCommand command = operation switch
        {
            "forward" => new BringForwardCommand(["b", "d"], document),
            "backward" => new SendBackwardCommand(["b", "d"], document),
            "front" => new BringToFrontCommand(["b", "d"], document),
            "back" => new SendToBackCommand(["b", "d"], document),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        LabelDocument reordered = command.Execute(document);

        Assert.Equal(expected.Split(','), reordered.Elements.Select(element => element.Id));
        LabelDocument restored = command.Undo(reordered);
        Assert.Equal(elements, restored.Elements);
        Assert.Equal(reordered.Elements, command.Execute(restored).Elements);
    }

    [Fact]
    public void ZOrderCommand_AtBoundaryIsDeterministicNoOp()
    {
        LabelDocument document = CreateDoc(
            RectangleElement.Create("a", MicrometreRect.Zero),
            RectangleElement.Create("b", MicrometreRect.Zero));
        BringToFrontCommand command = new(["b"], document);

        Assert.Same(document, command.Execute(document));
        Assert.Same(document, command.Undo(document));
    }
}
