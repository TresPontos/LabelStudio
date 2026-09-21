using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Transforms;

namespace LabelStudio.Editor.Tests;

public class SelectionTransformTests
{
    private static LabelDocument CreateDoc(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62, 0);
        MediaSnapshot snap = new("test", dims, MicrometreRect.Zero);
        return LabelDocument.Create(dims, "test", snap, elements);
    }

    private static RectangleElement Rect(string id, int x, int y, int w, int h) =>
        RectangleElement.Create(id, new(new(x), new(y), new(w), new(h)));

    [Fact]
    public void CombinedBounds_TwoElements_EncompassesBoth()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 2000, 5000, 3000),
            Rect("b", 7000, 1000, 5000, 4000));

        MicrometreRect bounds = SelectionBounds.GetCombinedBounds(doc, ["a", "b"]);

        Assert.Equal(1000, bounds.X.Value);
        Assert.Equal(1000, bounds.Y.Value);
        Assert.Equal(11000, bounds.Width.Value);
        Assert.Equal(4000, bounds.Height.Value);
    }

    [Fact]
    public void CombinedBounds_GroupPlusElement_CoversResolvedMembers()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 5000, 5000, 100, 100));
        doc = new GroupElementsCommand(["a", "b"], doc).Execute(doc);

        SelectionModel sel = new();
        sel.SelectGroup(doc.DesignMetadata.Groups[0].Id);
        sel.ToggleSelectElement("c");

        IReadOnlyList<string> resolved = SelectionBounds.ResolveTransformableElementIds(doc, sel);
        Assert.Contains("a", resolved);
        Assert.Contains("b", resolved);
        Assert.Contains("c", resolved);

        MicrometreRect bounds = SelectionBounds.GetCombinedBounds(doc, resolved);
        Assert.Equal(0, bounds.X.Value);
        Assert.Equal(0, bounds.Y.Value);
        Assert.Equal(5100, bounds.Width.Value);
        Assert.Equal(5100, bounds.Height.Value);
    }

    [Fact]
    public void CombinedBounds_HiddenElementExcludedWhenNotSelected()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 1000, 1000) with { IsVisible = false },
            Rect("b", 5000, 5000, 1000, 1000));

        MicrometreRect visibleOnly = SelectionBounds.GetCombinedBounds(doc, ["b"]);
        Assert.Equal(5000, visibleOnly.X.Value);
        Assert.Equal(5000, visibleOnly.Y.Value);
        Assert.Equal(1000, visibleOnly.Width.Value);
        Assert.Equal(1000, visibleOnly.Height.Value);
    }

    [Fact]
    public void GetTransformableElementIds_HiddenSelection_ReturnsEmpty()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 1000, 1000) with { IsVisible = false });

        SelectionModel sel = new();
        sel.SelectElement("a");

        Assert.Empty(sel.GetTransformableElementIds(doc));
    }

    [Fact]
    public void PlanMove_TwoElements_SameDeltaApplied()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 2000, 5000, 3000),
            Rect("b", 7000, 1000, 5000, 4000));

        ElementTransform[] transforms = SelectionTransformService.PlanMove(doc, ["a", "b"], 300, -200);

        Assert.Equal(2, transforms.Length);
        foreach (ElementTransform t in transforms)
        {
            Assert.Equal(300, t.After.Bounds.X.Value - t.Before.Bounds.X.Value);
            Assert.Equal(-200, t.After.Bounds.Y.Value - t.Before.Bounds.Y.Value);
        }
    }

    [Fact]
    public void PlanMove_GroupPlusElement_MembersMoveTogether()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 5000, 5000, 100, 100));
        doc = new GroupElementsCommand(["a", "b"], doc).Execute(doc);

        ElementTransform[] transforms = SelectionTransformService.PlanMove(doc, ["a", "b", "c"], 1000, 500);

        Assert.Equal(3, transforms.Length);
        LabelDocument moved = SelectionTransformService.ToCommand(transforms).Execute(doc);
        Assert.Equal(1000, moved.Elements[0].Bounds.X.Value);
        Assert.Equal(1200, moved.Elements[1].Bounds.X.Value);
        Assert.Equal(6000, moved.Elements[2].Bounds.X.Value);
    }

    [Fact]
    public void PlanMove_OneUndoEntry_ExactUndoRedo()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 1000, 5000, 3000),
            Rect("b", 8000, 8000, 2000, 2000));

        CommandHistory history = new();
        history.MarkSaved();
        ElementTransform[] transforms = SelectionTransformService.PlanMove(doc, ["a", "b"], 777, 333);
        doc = history.Push(SelectionTransformService.ToCommand(transforms), doc);

        Assert.Equal(1, history.UndoCount);
        Assert.True(history.IsDirty);

        doc = history.Undo(doc);
        Assert.Equal(1000, doc.Elements[0].Bounds.X.Value);
        Assert.Equal(8000, doc.Elements[1].Bounds.X.Value);

        doc = history.Redo(doc);
        Assert.Equal(1777, doc.Elements[0].Bounds.X.Value);
        Assert.Equal(8777, doc.Elements[1].Bounds.X.Value);
    }

    [Fact]
    public void PlanMove_RepeatedUndoRedo_NoDrift()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1234, 5678, 4321, 8765),
            Rect("b", 999, 111, 222, 333));

        CommandHistory history = new();
        ElementTransform[] transforms = SelectionTransformService.PlanMove(doc, ["a", "b"], 13, -17);
        doc = history.Push(SelectionTransformService.ToCommand(transforms), doc);

        for (int i = 0; i < 10; i++)
        {
            doc = history.Undo(doc);
            doc = history.Redo(doc);
        }

        Assert.Equal(1234 + 13, doc.Elements[0].Bounds.X.Value);
        Assert.Equal(5678 - 17, doc.Elements[0].Bounds.Y.Value);
        Assert.Equal(999 + 13, doc.Elements[1].Bounds.X.Value);
    }

    [Fact]
    public void PlanResize_CornerResize_ScalesRelativeGeometry()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 5000, 5000),
            Rect("b", 2500, 2500, 2500, 2500));

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a", "b"]);
        MicrometreRect doubled = new(original.X, original.Y, new(10000), new(10000));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a", "b"], original, doubled);

        LabelDocument resized = SelectionTransformService.ToCommand(transforms).Execute(doc);
        Assert.Equal(0, resized.Elements[0].Bounds.X.Value);
        Assert.Equal(10000, resized.Elements[0].Bounds.Width.Value);
        Assert.Equal(5000, resized.Elements[1].Bounds.X.Value);
        Assert.Equal(5000, resized.Elements[1].Bounds.Width.Value);
    }

    [Fact]
    public void PlanResize_OppositeAnchorStaysFixed()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 5000, 5000),
            Rect("b", 2500, 2500, 2500, 2500));

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a", "b"]);
        MicrometreRect resized = new(original.X, original.Y, new(5000), new(2000));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a", "b"], original, resized);

        LabelDocument result = SelectionTransformService.ToCommand(transforms).Execute(doc);
        Assert.Equal(0, result.Elements[0].Bounds.X.Value);
        Assert.Equal(0, result.Elements[0].Bounds.Y.Value);
    }

    [Fact]
    public void PlanResize_MinimumDimensions_Enforced()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 5000, 5000),
            Rect("b", 2500, 2500, 2500, 2500));

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a", "b"]);
        MicrometreRect tiny = new(original.X, original.Y, new(10), new(10));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a", "b"], original, tiny);

        foreach (ElementTransform t in transforms)
        {
            Assert.True(t.After.Bounds.Width.Value >= SelectionTransformService.MinElementWidthMicrometres);
            Assert.True(t.After.Bounds.Height.Value >= SelectionTransformService.MinElementHeightMicrometres);
        }
    }

    [Fact]
    public void PlanResize_NoNegativeGeometry()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 5000, 5000));

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a"]);
        MicrometreRect flipped = new(new(4000), new(4000), new(-1000), new(-1000));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a"], original, flipped);

        foreach (ElementTransform t in transforms)
        {
            Assert.True(t.After.Bounds.Width.Value > 0);
            Assert.True(t.After.Bounds.Height.Value > 0);
        }
    }

    [Fact]
    public void PlanResize_Proportional_PreservesAspect()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 5000, 2500),
            Rect("b", 2500, 1250, 2500, 1250));

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a", "b"]);
        MicrometreRect target = new(original.X, original.Y, new(20000), new(1000));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a", "b"], original, target, proportional: true);

        LabelDocument result = SelectionTransformService.ToCommand(transforms).Execute(doc);
        MicrometreRect newCombined = SelectionBounds.GetCombinedBounds(result, ["a", "b"]);
        double aspect = (double)newCombined.Width.Value / newCombined.Height.Value;
        double originalAspect = (double)original.Width.Value / original.Height.Value;
        Assert.Equal(originalAspect, aspect, 1);
    }

    [Fact]
    public void PlanResize_GroupMembershipAndZOrderUnchanged()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 5000, 5000, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        MicrometreRect original = SelectionBounds.GetCombinedBounds(doc, ["a", "b", "c"]);
        MicrometreRect target = new(original.X, original.Y, new(original.Width.Value * 2), new(original.Height.Value * 2));

        ElementTransform[] transforms = SelectionTransformService.PlanResize(doc, ["a", "b", "c"], original, target);
        LabelDocument result = SelectionTransformService.ToCommand(transforms).Execute(doc);

        Assert.Single(result.DesignMetadata.Groups);
        Assert.Equal(groupCmd.GroupId, result.DesignMetadata.Groups[0].Id);
        Assert.Equal(["a", "b"], result.DesignMetadata.Groups[0].MemberIds);
        Assert.Equal(doc.Elements.Select(e => e.Id), result.Elements.Select(e => e.Id));
    }

    [Fact]
    public void LockedSelection_BlocksEntireTransform()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100) with { IsLocked = true },
            Rect("b", 200, 0, 100, 100));

        SelectionModel sel = new();
        sel.SetElementSelection(["a", "b"]);

        IReadOnlyCollection<string> transformable = sel.GetTransformableElementIds(doc);
        Assert.Empty(transformable);
        Assert.True(SelectionBounds.ContainsLockedMember(doc, ["a", "b"]));
    }

    [Fact]
    public void LockedGroup_BlocksTransform()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        ElementGroup locked = new(groupCmd.GroupId, null, ["a", "b"], true, true);
        doc = doc with
        {
            DesignMetadata = new DocumentDesignMetadata(
                [locked], doc.DesignMetadata.Guides, doc.DesignMetadata.Grid),
        };

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);

        Assert.Empty(sel.GetTransformableElementIds(doc));
    }

    [Fact]
    public void GroupWithLockedMember_BlocksTransform()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        DocumentElement lockedA = doc.Elements.First(e => e.Id == "a") with { IsLocked = true };
        doc = doc.WithElements(doc.Elements.Select(e => e.Id == "a" ? lockedA : e).ToList().AsReadOnly());

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);

        Assert.Empty(sel.GetTransformableElementIds(doc));
    }

    [Fact]
    public void PlanMoveTo_SetsCombinedOrigin()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 2000, 5000, 3000),
            Rect("b", 7000, 1000, 5000, 4000));

        ElementTransform[] transforms = SelectionTransformService.PlanMoveTo(doc, ["a", "b"], 5000, 6000);

        LabelDocument moved = SelectionTransformService.ToCommand(transforms).Execute(doc);
        MicrometreRect combined = SelectionBounds.GetCombinedBounds(moved, ["a", "b"]);
        Assert.Equal(5000, combined.X.Value);
        Assert.Equal(6000, combined.Y.Value);
        Assert.Equal(11000, combined.Width.Value);
    }

    [Fact]
    public void PlanScaleToSize_SetsCombinedSize()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 5000, 5000),
            Rect("b", 5000, 5000, 5000, 5000));

        ElementTransform[] transforms = SelectionTransformService.PlanScaleToSize(doc, ["a", "b"], 20000, 8000);

        LabelDocument scaled = SelectionTransformService.ToCommand(transforms).Execute(doc);
        MicrometreRect combined = SelectionBounds.GetCombinedBounds(scaled, ["a", "b"]);
        Assert.Equal(20000, combined.Width.Value);
        Assert.Equal(8000, combined.Height.Value);
    }

    [Fact]
    public void PlanMove_Line_EndpointsMoveExactly()
    {
        LabelDocument doc = CreateDoc(
            new LineElement("l1",
                new MicrometrePoint(new(1000), new(2000)),
                new MicrometrePoint(new(6000), new(7000)),
                new Micrometre(200), InkChannel.Black));

        ElementTransform[] transforms = SelectionTransformService.PlanMove(doc, ["l1"], 500, 300);
        LabelDocument moved = SelectionTransformService.ToCommand(transforms).Execute(doc);

        LineElement line = Assert.IsType<LineElement>(moved.Elements[0]);
        Assert.Equal(1500, line.Start.X.Value);
        Assert.Equal(2300, line.Start.Y.Value);
        Assert.Equal(6500, line.End.X.Value);
        Assert.Equal(7300, line.End.Y.Value);
    }

    [Fact]
    public void DragTransaction_ManyPreviewUpdates_ProduceOneCommand()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 1000, 5000, 3000),
            Rect("b", 8000, 8000, 2000, 2000));

        DragTransaction drag = new();
        drag.Begin(["a", "b"], doc);

        var previews = new Dictionary<string, MicrometreRect>(StringComparer.Ordinal);
        for (int i = 1; i <= 14; i++)
        {
            previews["a"] = new(new(1000 + i * 10), new(1000), new(5000), new(3000));
            previews["b"] = new(new(8000 + i * 10), new(8000), new(2000), new(2000));
        }

        IEditorCommand? cmd = drag.Commit(doc, previews);
        Assert.NotNull(cmd);

        CommandHistory history = new();
        doc = history.Push(cmd!, doc);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void DragTransaction_Escape_ProducesNoCommand()
    {
        LabelDocument doc = CreateDoc(Rect("a", 1000, 1000, 5000, 3000));

        DragTransaction drag = new();
        drag.Begin(["a"], doc);
        drag.Cancel();

        IEditorCommand? cmd = drag.Commit(doc);
        Assert.Null(cmd);
    }

    [Fact]
    public void Nudge_MultiSelection_OneCommandPerKeypress()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 1000, 5000, 3000),
            Rect("b", 8000, 8000, 2000, 2000));

        CommandHistory history = new();
        history.MarkSaved();

        ElementTransform[] nudge = SelectionTransformService.PlanMove(doc, ["a", "b"], EditorState.NudgeSmallMicrometres, 0);
        doc = history.Push(SelectionTransformService.ToCommand(nudge), doc);

        Assert.Equal(1, history.UndoCount);
        Assert.Equal(1100, doc.Elements[0].Bounds.X.Value);
        Assert.Equal(8100, doc.Elements[1].Bounds.X.Value);
    }

    [Fact]
    public void Nudge_LargeShiftNudge_AppliesCorrectDelta()
    {
        LabelDocument doc = CreateDoc(Rect("a", 1000, 1000, 5000, 3000));

        ElementTransform[] nudge = SelectionTransformService.PlanMove(doc, ["a"], EditorState.NudgeLargeMicrometres, 0);
        LabelDocument moved = SelectionTransformService.ToCommand(nudge).Execute(doc);

        Assert.Equal(2000, moved.Elements[0].Bounds.X.Value);
    }
}