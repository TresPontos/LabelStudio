using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor.Tests;

public class SelectionTests
{
    [Fact]
    public void Select_SetsActiveId()
    {
        SelectionModel sel = new();
        sel.Select("r1");
        Assert.Equal("r1", sel.ActiveId);
        Assert.Single(sel.SelectedIds);
    }

    [Fact]
    public void ToggleSelect_AddsAndRemoves()
    {
        SelectionModel sel = new();
        sel.Select("r1");
        sel.ToggleSelect("r2");
        Assert.Equal(2, sel.Count);
        Assert.True(sel.IsMultiSelect);

        sel.ToggleSelect("r1");
        Assert.Single(sel.SelectedIds);
        Assert.DoesNotContain("r1", sel.SelectedIds);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        SelectionModel sel = new();
        sel.Select("r1");
        sel.ToggleSelect("r2");
        sel.Clear();
        Assert.Empty(sel.SelectedIds);
        Assert.Null(sel.ActiveId);
    }

    [Fact]
    public void SetSelection_ReplacesCurrent()
    {
        SelectionModel sel = new();
        sel.Select("r1");
        sel.SetSelection(["r2", "r3"]);
        Assert.Equal(2, sel.Count);
        Assert.Contains("r2", sel.SelectedIds);
        Assert.Contains("r3", sel.SelectedIds);
        Assert.DoesNotContain("r1", sel.SelectedIds);
    }

    [Fact]
    public void PruneDeleted_RemovesStaleIds()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62, 0);
        MediaSnapshot snap = new("test", dims, MicrometreRect.Zero);
        LabelDocument doc = LabelDocument.Create(dims, "test", snap,
            [RectangleElement.Create("r1", MicrometreRect.Zero),
             RectangleElement.Create("r2", MicrometreRect.Zero)]);

        SelectionModel sel = new();
        sel.Select("r1");
        sel.ToggleSelect("r2");

        LabelDocument reduced = doc.WithElements([doc.Elements[0]]);
        sel.PruneDeleted(reduced);

        Assert.Single(sel.SelectedIds);
        Assert.Contains("r1", sel.SelectedIds);
    }
}