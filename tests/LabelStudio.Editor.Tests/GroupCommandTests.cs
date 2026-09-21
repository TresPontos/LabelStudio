using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor.Tests;

public class GroupCommandTests
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
    public void Group_TwoAdjacentElements_CreatesContiguousBlock()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand cmd = new(["a", "b"], doc);

        LabelDocument result = cmd.Execute(doc);

        Assert.Single(result.DesignMetadata.Groups);
        Assert.Equal(["a", "b"], result.DesignMetadata.Groups[0].MemberIds);
        Assert.Equal(2, result.Elements.Count);
    }

    [Fact]
    public void Group_NonContiguousElements_NormalizesToContiguousBlockAtTopmostPosition()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100),
            Rect("d", 600, 0, 100, 100),
            Rect("e", 800, 0, 100, 100));

        GroupElementsCommand cmd = new(["b", "d"], doc);
        LabelDocument result = cmd.Execute(doc);

        Assert.Equal(["a", "c", "b", "d", "e"], result.Elements.Select(e => e.Id));
        Assert.Single(result.DesignMetadata.Groups);
        Assert.Equal(["b", "d"], result.DesignMetadata.Groups[0].MemberIds);
    }

    [Fact]
    public void Group_PreservesInternalMemberOrder()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100),
            Rect("d", 600, 0, 100, 100));

        GroupElementsCommand cmd = new(["b", "c"], doc);
        LabelDocument result = cmd.Execute(doc);

        List<string> ids = result.Elements.Select(e => e.Id).ToList();
        int bIndex = ids.IndexOf("b");
        int cIndex = ids.IndexOf("c");
        Assert.True(bIndex < cIndex);
        Assert.Equal(["b", "c"], result.DesignMetadata.Groups[0].MemberIds);
    }

    [Fact]
    public void Group_ReceivesStableId()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand cmd = new(["a", "b"], doc);

        Assert.False(string.IsNullOrWhiteSpace(cmd.GroupId));

        LabelDocument result = cmd.Execute(doc);
        Assert.Equal(cmd.GroupId, result.DesignMetadata.Groups[0].Id);
    }

    [Fact]
    public void Group_ThenUndo_RestoresExactOriginalState()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100));

        GroupElementsCommand cmd = new(["a", "c"], doc);
        LabelDocument grouped = cmd.Execute(doc);
        LabelDocument undone = cmd.Undo(grouped);

        Assert.Equal(doc.Elements.Select(e => e.Id), undone.Elements.Select(e => e.Id));
        Assert.Empty(undone.DesignMetadata.Groups);
    }

    [Fact]
    public void Group_ThenUndo_ThenRedo_ReconstructsSameGroupIdAndMembership()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));

        GroupElementsCommand cmd = new(["a", "b"], doc);
        LabelDocument grouped = cmd.Execute(doc);
        string groupId = cmd.GroupId;
        string[] memberIds = grouped.DesignMetadata.Groups[0].MemberIds.ToArray();

        LabelDocument undone = cmd.Undo(grouped);
        LabelDocument redone = cmd.Execute(undone);

        Assert.Single(redone.DesignMetadata.Groups);
        Assert.Equal(groupId, redone.DesignMetadata.Groups[0].Id);
        Assert.Equal(memberIds, redone.DesignMetadata.Groups[0].MemberIds);
    }

    [Fact]
    public void Group_RejectsAlreadyGroupedElement()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100), Rect("c", 400, 0, 100, 100));
        GroupElementsCommand cmd1 = new(["a", "b"], doc);
        doc = cmd1.Execute(doc);

        Assert.Throws<InvalidOperationException>(() => new GroupElementsCommand(["b", "c"], doc));
    }

    [Fact]
    public void Group_RequiresAtLeastTwoElements()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100));
        Assert.Throws<ArgumentException>(() => new GroupElementsCommand(["a"], doc));
    }

    [Fact]
    public void Ungroup_RemovesGroupRecord_LeavesElementsInPlace()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100), Rect("c", 400, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        UngroupElementsCommand ungroupCmd = new(groupCmd.GroupId, doc);
        LabelDocument result = ungroupCmd.Execute(doc);

        Assert.Empty(result.DesignMetadata.Groups);
        Assert.Equal(3, result.Elements.Count);
    }

    [Fact]
    public void Ungroup_PreservesGeometryIdsAndOrder()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100));

        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        MicrometreRect aBounds = doc.Elements.First(e => e.Id == "a").Bounds;
        MicrometreRect bBounds = doc.Elements.First(e => e.Id == "b").Bounds;

        UngroupElementsCommand ungroupCmd = new(groupCmd.GroupId, doc);
        LabelDocument result = ungroupCmd.Execute(doc);

        Assert.Equal(aBounds, result.Elements.First(e => e.Id == "a").Bounds);
        Assert.Equal(bBounds, result.Elements.First(e => e.Id == "b").Bounds);
    }

    [Fact]
    public void Ungroup_ThenUndo_RestoresExactGroup()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);
        string groupId = groupCmd.GroupId;

        UngroupElementsCommand ungroupCmd = new(groupId, doc);
        LabelDocument ungrouped = ungroupCmd.Execute(doc);
        LabelDocument undone = ungroupCmd.Undo(ungrouped);

        Assert.Single(undone.DesignMetadata.Groups);
        Assert.Equal(groupId, undone.DesignMetadata.Groups[0].Id);
        Assert.Equal(["a", "b"], undone.DesignMetadata.Groups[0].MemberIds);
    }

    [Fact]
    public void GroupMove_AppliesDeltaToAllMembers_OneUndoEntry()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 1000, 5000, 3000),
            Rect("b", 7000, 1000, 5000, 3000),
            Rect("c", 13000, 1000, 5000, 3000));

        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);
        ElementGroup group = doc.DesignMetadata.Groups[0];

        var changes = new Dictionary<string, (MicrometreRect, MicrometreRect)>();
        foreach (string id in group.MemberIds)
        {
            DocumentElement elem = doc.Elements.First(e => e.Id == id);
            changes[id] = (elem.Bounds, elem.Bounds.Offset(new(300), new(-200)));
        }
        CommandHistory history = new();
        doc = history.Push(new MoveElementsCommand(changes), doc);

        Assert.Equal(1300, doc.Elements.First(e => e.Id == "a").Bounds.X.Value);
        Assert.Equal(7300, doc.Elements.First(e => e.Id == "b").Bounds.X.Value);

        doc = history.Undo(doc);
        Assert.Equal(1000, doc.Elements.First(e => e.Id == "a").Bounds.X.Value);
        Assert.Equal(7000, doc.Elements.First(e => e.Id == "b").Bounds.X.Value);
    }

    [Fact]
    public void ZOrder_BringForward_MovesGroupAsContiguousBlock()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100),
            Rect("d", 600, 0, 100, 100));

        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        BringForwardCommand bringCmd = new(["a", "b"], doc);
        doc = bringCmd.Execute(doc);

        List<string> ids = doc.Elements.Select(e => e.Id).ToList();
        int aIdx = ids.IndexOf("a");
        int bIdx = ids.IndexOf("b");
        Assert.Equal(bIdx - 1, aIdx);
        Assert.True(aIdx > ids.IndexOf("c"));
    }

    [Fact]
    public void ZOrder_BringToFront_PreservesInternalMemberOrder()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100),
            Rect("d", 600, 0, 100, 100));

        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        BringToFrontCommand frontCmd = new(["a", "b"], doc);
        doc = frontCmd.Execute(doc);

        List<string> ids = doc.Elements.Select(e => e.Id).ToList();
        Assert.Equal(["c", "d", "a", "b"], ids);
    }

    [Fact]
    public void Selection_ClickGroupedMember_ResolvesToGroup()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);

        Assert.True(sel.ContainsGroup(groupCmd.GroupId));
        Assert.False(sel.ContainsElement("a"));
        IReadOnlyCollection<string> elementIds = sel.GetSelectedElementIds(doc);
        Assert.Contains("a", elementIds);
        Assert.Contains("b", elementIds);
    }

    [Fact]
    public void Selection_NoDuplicateChildAndGroup()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);
        sel.SetSelection([SelectionTarget.Group(groupCmd.GroupId)]);

        Assert.Equal(1, sel.Count);
        Assert.True(sel.ContainsGroup(groupCmd.GroupId));
        Assert.False(sel.ContainsElement("a"));
        Assert.False(sel.ContainsElement("b"));

        IReadOnlyCollection<string> elementIds = sel.GetSelectedElementIds(doc);
        Assert.Contains("a", elementIds);
        Assert.Contains("b", elementIds);
    }

    [Fact]
    public void GroupGeometry_DerivesBoundsFromMembers()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 1000, 2000, 5000, 3000),
            Rect("b", 7000, 1000, 5000, 4000));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        MicrometreRect bounds = GroupGeometry.GetGroupBounds(doc, groupCmd.GroupId);

        Assert.Equal(1000, bounds.X.Value);
        Assert.Equal(1000, bounds.Y.Value);
        Assert.Equal(11000, bounds.Width.Value);
        Assert.Equal(4000, bounds.Height.Value);
    }

    [Fact]
    public void LockedGroup_CannotBeTransformed()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        ElementGroup lockedGroup = new(groupCmd.GroupId, null, ["a", "b"], true, true);
        List<ElementGroup> groups = [lockedGroup];
        doc = doc with { DesignMetadata = new DocumentDesignMetadata(groups, doc.DesignMetadata.Guides, doc.DesignMetadata.Grid) };

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);

        Assert.True(sel.HasLockedMembers(doc));
        Assert.Empty(sel.GetTransformableElementIds(doc));
    }

    [Fact]
    public void LockedMember_PreventsGroupTransform()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        DocumentElement lockedA = doc.Elements.First(e => e.Id == "a") with { IsLocked = true };
        doc = doc.WithElements(doc.Elements.Select(e => e.Id == "a" ? lockedA : e).ToList().AsReadOnly());

        SelectionModel sel = new();
        sel.SelectGroup(groupCmd.GroupId);

        Assert.True(sel.HasLockedMembers(doc));
    }

    [Fact]
    public void HiddenMember_RemainsHiddenInGroup()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        DocumentElement hiddenA = doc.Elements.First(e => e.Id == "a") with { IsVisible = false };
        doc = doc.WithElements(doc.Elements.Select(e => e.Id == "a" ? hiddenA : e).ToList().AsReadOnly());

        Assert.False(doc.IsEffectivelyVisible("a"));
        Assert.True(doc.IsEffectivelyVisible("b"));
    }
}