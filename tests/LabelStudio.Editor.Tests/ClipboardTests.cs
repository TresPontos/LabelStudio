using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Clipboard;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor.Tests;

public class ClipboardTests
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
    public void SerializeDeserialize_RoundTripsElements()
    {
        LabelDocument doc = CreateDoc(
            Rect("r1", 1000, 2000, 5000, 3000) with { Name = "TestRect", IsLocked = true },
            new TextElement("t1", new(new(1000), new(6000), new(20000), new(3000)),
                InkChannel.Red, "Hello", 24, null) with
            { RotationMillidegrees = 45000 });

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1"), SelectionTarget.Element("t1")]);

        string json = ClipboardSerializer.Serialize(payload);
        ClipboardPayload parsed = ClipboardSerializer.Deserialize(json);

        Assert.Equal(2, parsed.Elements.Count);
        Assert.Equal("r1", parsed.Elements[0].Id);
        Assert.Equal("TestRect", parsed.Elements[0].Name);
        Assert.True(parsed.Elements[0].IsLocked);
        Assert.Equal(45000, parsed.Elements[1].RotationMillidegrees);
        Assert.Equal(InkChannel.Red, parsed.Elements[1].Ink);
    }

    [Fact]
    public void Paste_GeneratesNewElementIds()
    {
        LabelDocument doc = CreateDoc(Rect("r1", 1000, 1000, 5000, 3000));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1")]);

        PastePlan plan = service.PlanPaste(payload, doc);
        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        LabelDocument result = cmd.Execute(doc);

        Assert.NotEqual("r1", result.Elements[1].Id);
        Assert.Single(result.Elements, e => e.Id == "r1");
    }

    [Fact]
    public void Paste_AppliesOffsetToGeometry()
    {
        LabelDocument doc = CreateDoc(Rect("r1", 10000, 10000, 5000, 3000));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1")]);

        PastePlan plan = service.PlanPaste(payload, doc);
        Assert.Equal(12000, plan.NewElements[0].Bounds.X.Value);
        Assert.Equal(12000, plan.NewElements[0].Bounds.Y.Value);
    }

    [Fact]
    public void RepeatedPaste_AppliesIncrementalOffset()
    {
        LabelDocument doc = CreateDoc(Rect("r1", 10000, 10000, 5000, 3000));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1")]);

        PastePlan plan1 = service.PlanPaste(payload, doc, pasteCount: 0);
        Assert.Equal(12000, plan1.NewElements[0].Bounds.X.Value);

        PastePlan plan2 = service.PlanPaste(payload, doc, pasteCount: 1);
        Assert.Equal(14000, plan2.NewElements[0].Bounds.X.Value);

        PastePlan plan3 = service.PlanPaste(payload, doc, pasteCount: 2);
        Assert.Equal(16000, plan3.NewElements[0].Bounds.X.Value);
    }

    [Fact]
    public void Paste_UndoRedo_RestoresExactIds()
    {
        LabelDocument doc = CreateDoc(Rect("r1", 1000, 1000, 5000, 3000));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1")]);

        PastePlan plan = service.PlanPaste(payload, doc);
        CommandHistory history = new();
        history.MarkSaved();

        doc = history.Push(new PasteElementsCommand(plan.NewElements, plan.NewGroups, doc), doc);
        string pastedId = doc.Elements[1].Id;
        Assert.Equal(2, doc.Elements.Count);

        doc = history.Undo(doc);
        Assert.Single(doc.Elements);

        doc = history.Redo(doc);
        Assert.Equal(2, doc.Elements.Count);
        Assert.Equal(pastedId, doc.Elements[1].Id);
    }

    [Fact]
    public void Paste_PlacesContentAtTopOfZOrder()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("a")]);

        PastePlan plan = service.PlanPaste(payload, doc);
        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        LabelDocument result = cmd.Execute(doc);

        Assert.Equal(3, result.Elements.Count);
        Assert.NotEqual("a", result.Elements[2].Id);
    }

    [Fact]
    public void Duplicate_GeneratesNewIdsAndPreservesGeometry()
    {
        LabelDocument doc = CreateDoc(
            Rect("r1", 5000, 5000, 3000, 2000),
            Rect("r2", 10000, 5000, 3000, 2000));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1"), SelectionTarget.Element("r2")]);

        PastePlan plan = service.PlanPaste(payload, doc);
        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        LabelDocument result = cmd.Execute(doc);

        Assert.Equal(4, result.Elements.Count);
        Assert.NotEqual("r1", result.Elements[2].Id);
        Assert.NotEqual("r2", result.Elements[3].Id);
        Assert.Equal(7000, result.Elements[2].Bounds.X.Value);
        Assert.Equal(12000, result.Elements[3].Bounds.X.Value);
    }

    [Fact]
    public void CopyGroup_IncludesMembersAndGroupRecord()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Group(groupCmd.GroupId)]);

        Assert.Single(payload.Groups);
        Assert.Equal(2, payload.Elements.Count);
        Assert.Contains("a", payload.Elements.Select(e => e.Id));
        Assert.Contains("b", payload.Elements.Select(e => e.Id));
    }

    [Fact]
    public void PasteGroup_RemapsGroupAndMemberIds()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Group(groupCmd.GroupId)]);
        PastePlan plan = service.PlanPaste(payload, doc);

        Assert.NotEqual(groupCmd.GroupId, plan.NewGroups[0].Id);
        Assert.Equal(2, plan.NewGroups[0].MemberIds.Count);
        Assert.NotEqual("a", plan.NewGroups[0].MemberIds[0]);
        Assert.NotEqual("b", plan.NewGroups[0].MemberIds[1]);

        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        LabelDocument result = cmd.Execute(cmd.Undo(doc));

        Assert.Equal(2, result.DesignMetadata.Groups.Count);
        Assert.NotEqual(groupCmd.GroupId, result.DesignMetadata.Groups[1].Id);
    }

    [Fact]
    public void PasteGroup_RemainsContiguous()
    {
        LabelDocument doc = CreateDoc(
            Rect("a", 0, 0, 100, 100),
            Rect("b", 200, 0, 100, 100),
            Rect("c", 400, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Group(groupCmd.GroupId)]);
        PastePlan plan = service.PlanPaste(payload, doc);
        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        LabelDocument result = cmd.Execute(doc);

        List<string> ids = result.Elements.Select(e => e.Id).ToList();
        string newMember1 = plan.NewGroups[0].MemberIds[0];
        string newMember2 = plan.NewGroups[0].MemberIds[1];
        int idx1 = ids.IndexOf(newMember1);
        int idx2 = ids.IndexOf(newMember2);
        Assert.Equal(idx2 - 1, idx1);
    }

    [Fact]
    public void CopyIndividualGroupMember_PastesUngrouped()
    {
        LabelDocument doc = CreateDoc(Rect("a", 0, 0, 100, 100), Rect("b", 200, 0, 100, 100));
        GroupElementsCommand groupCmd = new(["a", "b"], doc);
        doc = groupCmd.Execute(doc);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("a")]);
        PastePlan plan = service.PlanPaste(payload, doc);

        Assert.Empty(plan.NewGroups);
        Assert.Single(plan.NewElements);
    }

    [Fact]
    public void SameDocumentImagePaste_ReusesAssetId()
    {
        ImageElement img = new("i1", new(new(1000), new(1000), new(5000), new(5000)), InkChannel.Black, "asset-001");
        LabelDocument doc = CreateDoc(img);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("i1")]);
        PastePlan plan = service.PlanPaste(payload, doc);

        Assert.Equal("asset-001", (plan.NewElements[0] as ImageElement)!.AssetId);
    }

    [Fact]
    public void CrossDocumentImagePaste_TransfersAssetBytes()
    {
        byte[] assetBytes = [1, 2, 3, 4, 5];
        ImageElement img = new("i1", new(new(1000), new(1000), new(5000), new(5000)), InkChannel.Black, "asset-001");
        LabelDocument sourceDoc = CreateDoc(img);

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(
            sourceDoc,
            [SelectionTarget.Element("i1")],
            _ => assetBytes);

        Assert.Single(payload.Assets);
        Assert.Equal("asset-001", payload.Assets[0].SourceAssetId);
        Assert.Equal(assetBytes, payload.Assets[0].Bytes);

        LabelDocument destDoc = CreateDoc();
        Dictionary<string, byte[]> destAssets = new();
        PastePlan plan = service.PlanPaste(
            payload,
            destDoc,
            destinationAssetProvider: null,
            assetImporter: (srcId, hash, bytes) =>
            {
                string newId = "new-asset";
                destAssets[newId] = bytes;
                return newId;
            });

        ImageElement pastedImg = Assert.IsType<ImageElement>(plan.NewElements[0]);
        Assert.NotEqual("asset-001", pastedImg.AssetId);
        Assert.True(destAssets.ContainsKey(pastedImg.AssetId));
        Assert.Equal(assetBytes, destAssets[pastedImg.AssetId]);
    }

    [Fact]
    public void AssetDeduplication_ByContentHash_ReusesExisting()
    {
        byte[] assetBytes = [10, 20, 30];
        ImageElement srcImg = new("s1", new(new(0), new(0), new(100), new(100)), InkChannel.Black, "src-asset");
        LabelDocument sourceDoc = CreateDoc(srcImg);

        ImageElement destImg = new("d1", new(new(5000), new(0), new(100), new(100)), InkChannel.Black, "dest-asset");
        LabelDocument destDoc = CreateDoc(destImg);
        Dictionary<string, byte[]> destAssets = new() { ["dest-asset"] = assetBytes };

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(
            sourceDoc,
            [SelectionTarget.Element("s1")],
            _ => assetBytes);

        PastePlan plan = service.PlanPaste(
            payload,
            destDoc,
            destinationAssetProvider: id => destAssets.GetValueOrDefault(id),
            assetImporter: (_, _, bytes) => throw new InvalidOperationException("Should not import - should deduplicate."));

        ImageElement pastedImg = Assert.IsType<ImageElement>(plan.NewElements[0]);
        Assert.Equal("dest-asset", pastedImg.AssetId);
    }

    [Fact]
    public void AssetDeduplication_SameIdDifferentContent_DoesNotReuse()
    {
        byte[] srcBytes = [10, 20, 30];
        byte[] destBytes = [99, 99, 99];
        ImageElement srcImg = new("s1", new(new(0), new(0), new(100), new(100)), InkChannel.Black, "shared-id");
        LabelDocument sourceDoc = CreateDoc(srcImg);

        ImageElement destImg = new("d1", new(new(5000), new(0), new(100), new(100)), InkChannel.Black, "shared-id");
        LabelDocument destDoc = CreateDoc(destImg);
        Dictionary<string, byte[]> destAssets = new() { ["shared-id"] = destBytes };

        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(
            sourceDoc,
            [SelectionTarget.Element("s1")],
            _ => srcBytes);

        bool importCalled = false;
        PastePlan plan = service.PlanPaste(
            payload,
            destDoc,
            destinationAssetProvider: id => destAssets.GetValueOrDefault(id),
            assetImporter: (_, _, _) => { importCalled = true; return "new-asset"; });

        Assert.True(importCalled);
        ImageElement pastedImg = Assert.IsType<ImageElement>(plan.NewElements[0]);
        Assert.NotEqual("shared-id", pastedImg.AssetId);
    }

    [Fact]
    public void ClipboardValidation_UnsupportedSchema_Throws()
    {
        string json = """{"schema":"unknown","version":1,"elements":[]}""";
        Assert.Throws<InvalidDataException>(() => ClipboardSerializer.Deserialize(json));
    }

    [Fact]
    public void ClipboardValidation_FutureVersion_Throws()
    {
        string json = """{"schema":"labelstudio.clipboard","version":99,"elements":[]}""";
        Assert.Throws<InvalidDataException>(() => ClipboardSerializer.Deserialize(json));
    }

    [Fact]
    public void ClipboardValidation_AssetHashMismatch_Throws()
    {
        LabelDocument doc = CreateDoc();
        ClipboardPayload payload = new(
            ClipboardPayload.SchemaName,
            ClipboardPayload.CurrentVersion,
            "test",
            [],
            [],
            [new ClipboardAsset("a1", "wronghash", [1, 2, 3])]);

        SelectionCloneService service = new();
        Assert.Throws<InvalidDataException>(() => service.PlanPaste(payload, doc));
    }

    [Fact]
    public void Paste_VisibilityAndLockPreserved()
    {
        LabelDocument doc = CreateDoc(
            Rect("r1", 1000, 1000, 5000, 3000) with { IsVisible = false, IsLocked = true });
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("r1")]);
        PastePlan plan = service.PlanPaste(payload, doc);

        Assert.False(plan.NewElements[0].IsVisible);
        Assert.True(plan.NewElements[0].IsLocked);
    }

    [Fact]
    public void Paste_LineGeometry_OffsetCorrectly()
    {
        LabelDocument doc = CreateDoc(
            new LineElement("l1",
                new MicrometrePoint(new(1000), new(2000)),
                new MicrometrePoint(new(5000), new(6000)),
                new Micrometre(200), InkChannel.Black));
        SelectionCloneService service = new();
        ClipboardPayload payload = service.BuildPayload(doc, [SelectionTarget.Element("l1")]);
        PastePlan plan = service.PlanPaste(payload, doc);

        LineElement pasted = Assert.IsType<LineElement>(plan.NewElements[0]);
        Assert.Equal(3000, pasted.Start.X.Value);
        Assert.Equal(4000, pasted.Start.Y.Value);
        Assert.Equal(7000, pasted.End.X.Value);
        Assert.Equal(8000, pasted.End.Y.Value);
    }
}