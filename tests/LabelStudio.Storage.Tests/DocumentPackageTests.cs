using System.IO.Compression;
using System.Text;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Storage.Migrations;

namespace LabelStudio.Storage.Tests;

public class DocumentPackageTests
{
    private static string GetTempPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LabelStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test" + DocumentPackage.Extension);
    }

    private static LabelDocument CreateSampleDocument()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 48.26);
        MediaSnapshot snap = new(
            "brother.dk-22251",
            dims,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)));
        RectangleElement rect = RectangleElement.Create("r1",
            new(new(1000), new(1000), new(10000), new(5000))) with
        {
            Name = "Border",
            IsVisible = false,
            IsLocked = true,
            RotationMillidegrees = 45000,
        };
        LineElement line = new("l1",
            new(new(1000), new(7000)),
            new(new(11000), new(7000)),
            new(200), InkChannel.Black);
        TextElement text = new("t1",
            new(new(1000), new(8000), new(20000), new(3000)),
            InkChannel.Black, "Hello", 24, null)
        {
            FrameSizing = TextFrameSizingMode.AutoHeight,
            Wrapping = TextWrappingMode.Wrap,
            Overflow = TextOverflowMode.ShrinkToFit,
            HorizontalAlignment = TextHorizontalAlignment.Center,
            VerticalAlignment = TextVerticalAlignment.Bottom,
        };
        ImageElement img = new("i1",
            new(new(25000), new(1000), new(10000), new(10000)),
            InkChannel.Black, "asset-001")
        { LockAspectRatio = false };

        DocumentDesignMetadata design = new(
            [new ElementGroup("group-1", "Main", ["r1", "t1"], false, true)],
            [new DocumentGuide("guide-1", DocumentGuideOrientation.Vertical, new(12500), "Center")],
            new DocumentGridGeometry(new(2000), new(3000), new(new(100), new(200)), 5),
            new DocumentSafeMargins(new(1000), new(2000), new(3000), new(4000)));
        return LabelDocument.Create(dims, "brother.dk-22251", snap, [rect, line, text, img],
            designMetadata: design, mediaKind: DocumentMediaKind.Continuous);
    }

    [Fact]
    public void SaveLoad_RoundTripsAllElementTypes()
    {
        string path = GetTempPath();
        LabelDocument original = CreateSampleDocument();

        DocumentPackage.Save(original, path);
        LabelDocument loaded = DocumentPackage.Load(path);

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.FormatVersion, loaded.FormatVersion);
        Assert.Equal(original.PageDimensions, loaded.PageDimensions);
        Assert.Equal(original.MediaKind, loaded.MediaKind);
        Assert.Equal(original.MediaProfileId, loaded.MediaProfileId);
        Assert.Equal(original.PrintDefaults, loaded.PrintDefaults);
        Assert.Equal(original.Elements.Count, loaded.Elements.Count);

        RectangleElement origRect = (RectangleElement)original.Elements[0];
        RectangleElement loadRect = (RectangleElement)loaded.Elements[0];
        Assert.Equal(origRect.Id, loadRect.Id);
        Assert.Equal(origRect.Bounds, loadRect.Bounds);
        Assert.Equal(origRect.Ink, loadRect.Ink);
        Assert.Equal(origRect.Fill, loadRect.Fill);
        Assert.Equal(origRect.StrokeWidth, loadRect.StrokeWidth);
        Assert.Equal(origRect.Name, loadRect.Name);
        Assert.Equal(origRect.IsVisible, loadRect.IsVisible);
        Assert.Equal(origRect.IsLocked, loadRect.IsLocked);
        Assert.Equal(origRect.RotationMillidegrees, loadRect.RotationMillidegrees);

        LineElement origLine = (LineElement)original.Elements[1];
        LineElement loadLine = (LineElement)loaded.Elements[1];
        Assert.Equal(origLine.Start, loadLine.Start);
        Assert.Equal(origLine.End, loadLine.End);
        Assert.Equal(origLine.Thickness, loadLine.Thickness);

        TextElement origText = (TextElement)original.Elements[2];
        TextElement loadText = (TextElement)loaded.Elements[2];
        Assert.Equal(origText.Text, loadText.Text);
        Assert.Equal(origText.FontSizePoints, loadText.FontSizePoints);
        Assert.Equal(origText.FrameSizing, loadText.FrameSizing);
        Assert.Equal(origText.Wrapping, loadText.Wrapping);
        Assert.Equal(origText.Overflow, loadText.Overflow);
        Assert.Equal(origText.HorizontalAlignment, loadText.HorizontalAlignment);
        Assert.Equal(origText.VerticalAlignment, loadText.VerticalAlignment);

        ImageElement origImg = (ImageElement)original.Elements[3];
        ImageElement loadImg = (ImageElement)loaded.Elements[3];
        Assert.Equal(origImg.AssetId, loadImg.AssetId);
        Assert.Equal(origImg.LockAspectRatio, loadImg.LockAspectRatio);

        Assert.Equal("group-1", loaded.DesignMetadata.Groups[0].Id);
        Assert.Equal(["r1", "t1"], loaded.DesignMetadata.Groups[0].MemberIds);
        Assert.False(loaded.DesignMetadata.Groups[0].IsVisible);
        Assert.True(loaded.DesignMetadata.Groups[0].IsLocked);
        Assert.Equal(DocumentGuideOrientation.Vertical, loaded.DesignMetadata.Guides[0].Orientation);
        Assert.Equal(new Micrometre(12500), loaded.DesignMetadata.Guides[0].Position);
        Assert.Equal(new Micrometre(2000), loaded.DesignMetadata.Grid.XSpacing);
        Assert.Equal(new MicrometrePoint(new(100), new(200)), loaded.DesignMetadata.Grid.Origin);
        Assert.Equal(original.DesignMetadata.SafeMargins, loaded.DesignMetadata.SafeMargins);
    }

    [Fact]
    public void SaveLoad_PreservesMediaGeometry()
    {
        string path = GetTempPath();
        PhysicalSize dims = PhysicalSize.FromMillimetres(17.0, 54.0);
        MediaSnapshot snap = new("brother.dk-11204", dims,
            new MicrometreRect(new(1500), new(3000), new(14000), new(47900)));

        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-11204", snap);
        DocumentPackage.Save(doc, path);
        LabelDocument loaded = DocumentPackage.Load(path);

        Assert.Equal(snap.ProfileId, loaded.MediaGeometry.ProfileId);
        Assert.Equal(snap.PhysicalDimensions, loaded.MediaGeometry.PhysicalDimensions);
        Assert.Equal(snap.PrintableArea, loaded.MediaGeometry.PrintableArea);
    }

    [Fact]
    public void Save_CreatesAtomicTempFile()
    {
        string path = GetTempPath();
        LabelDocument doc = CreateSampleDocument();
        DocumentPackage.Save(doc, path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        string path = GetTempPath();
        LabelDocument doc1 = CreateSampleDocument();
        DocumentPackage.Save(doc1, path);
        Thread.Sleep(10);
        LabelDocument doc2 = CreateSampleDocument();
        DocumentPackage.Save(doc2, path);

        LabelDocument loaded = DocumentPackage.Load(path);
        Assert.Equal(doc2.Id, loaded.Id);
    }

    [Fact]
    public void Load_WithRedInk_DeserializesCorrectly()
    {
        string path = GetTempPath();
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims, MicrometreRect.Zero);
        RectangleElement redRect = RectangleElement.Create("r1",
            new(new(1000), new(1000), new(10000), new(5000)),
            InkChannel.Red);

        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-22251", snap, [redRect]);
        DocumentPackage.Save(doc, path);
        LabelDocument loaded = DocumentPackage.Load(path);

        Assert.Equal(InkChannel.Red, loaded.Elements[0].Ink);
    }

    [Fact]
    public void Load_MissingDocument_ThrowsInvalidData()
    {
        string path = GetTempPath();
        using (FileStream fs = File.Create(path))
        using (ZipArchive archive = new(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            archive.CreateEntry("other.txt");
        }

        Assert.Throws<InvalidDataException>(() => DocumentPackage.Load(path));
    }

    [Fact]
    public void Load_V1Package_AutomaticallyMigratesToCurrentVersion()
    {
        string path = GetTempPath();
        string json = """
            {
              "formatVersion": 1,
              "id": "00000000-0000-0000-0000-000000000001",
              "pageDimensions": { "widthMicrometres": 62000, "heightMicrometres": 0 },
              "mediaProfileId": "test",
              "mediaGeometry": {
                "profileId": "test",
                "physicalDimensions": { "widthMicrometres": 62000, "heightMicrometres": 0 },
                "printableArea": { "x": 0, "y": 0, "width": 62000, "height": 0 }
              },
              "elements": [
                { "id": "i1", "type": "image", "bounds": { "x": 0, "y": 0, "width": 1000, "height": 1000 }, "ink": "black", "assetId": "image.bin" }
              ]
            }
            """;
        CreatePackage(path, json);

        LabelDocument loaded = DocumentPackage.Load(path);

        Assert.Equal(LabelDocument.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Empty(loaded.DesignMetadata.Groups);
        Assert.Equal(DocumentGridGeometry.Default, loaded.DesignMetadata.Grid);
        Assert.Equal(DocumentMediaKind.Continuous, loaded.MediaKind);
        Assert.Equal(new Micrometre(48_260), loaded.PageDimensions.Height);
        Assert.Equal(DocumentSafeMargins.Uniform(new Micrometre(1_000)), loaded.DesignMetadata.SafeMargins);
        Assert.True(loaded.Elements[0].IsVisible);
        Assert.True(((ImageElement)loaded.Elements[0]).LockAspectRatio);
    }

    [Fact]
    public void Load_FutureVersion_Throws()
    {
        string path = GetTempPath();
        CreatePackage(path, """{"formatVersion":99}""");

        Assert.Throws<UnsupportedDocumentVersionException>(() => DocumentPackage.Load(path));
    }

    [Fact]
    public void SaveLoadContent_PreservesAssetsAndPreviewExactly()
    {
        string path = GetTempPath();
        string secondPath = GetTempPath();
        byte[] asset = [0, 1, 2, 127, 128, 255];
        byte[] preview = [137, 80, 78, 71, 0, 255];
        DocumentPackageContent content = new(
            CreateSampleDocument(),
            new Dictionary<string, byte[]> { ["asset-001"] = asset },
            preview);

        DocumentPackage.SaveContent(content, path);
        DocumentPackageContent loaded = DocumentPackage.LoadContent(path);
        DocumentPackage.SaveContent(loaded, secondPath);
        DocumentPackageContent reloaded = DocumentPackage.LoadContent(secondPath);

        Assert.Equal(asset, loaded.Assets["asset-001"]);
        Assert.Equal(preview, loaded.PreviewPng);
        Assert.Equal(asset, reloaded.Assets["asset-001"]);
        Assert.Equal(preview, reloaded.PreviewPng);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("folder/file")]
    [InlineData("folder\\file")]
    [InlineData("preview.png")]
    [InlineData("CON")]
    public void SaveContent_InvalidAssetId_Throws(string assetId)
    {
        DocumentPackageContent content = new(
            CreateSampleDocument(),
            new Dictionary<string, byte[]> { [assetId] = [1] },
            null);

        Assert.Throws<ArgumentException>(() => DocumentPackage.SaveContent(content, GetTempPath()));
    }

    [Fact]
    public void SaveContent_OversizedAsset_Throws()
    {
        DocumentPackageContent content = new(
            CreateSampleDocument(),
            new Dictionary<string, byte[]> { ["large.bin"] = new byte[DocumentPackage.MaxAssetBytes + 1] },
            null);

        Assert.Throws<ArgumentException>(() => DocumentPackage.SaveContent(content, GetTempPath()));
    }

    [Fact]
    public void SaveContent_StaleDocumentVersion_Throws()
    {
        DocumentPackageContent content = new(
            CreateSampleDocument() with { FormatVersion = 1 },
            new Dictionary<string, byte[]>(),
            null);

        Assert.Throws<InvalidOperationException>(() => DocumentPackage.SaveContent(content, GetTempPath()));
    }

    [Fact]
    public void SaveLoad_PreservesGroupRecordsAndOrdering()
    {
        string path = GetTempPath();
        LabelDocument doc = CreateSampleDocument();
        string groupId = Guid.NewGuid().ToString("D");
        ElementGroup group = new(groupId, "Test Group", ["r1", "t1"], true, false);
        DocumentDesignMetadata metadata = new([group], [], doc.DesignMetadata.Grid);
        doc = doc with { DesignMetadata = metadata };

        DocumentPackage.Save(doc, path);
        LabelDocument loaded = DocumentPackage.Load(path);

        Assert.Single(loaded.DesignMetadata.Groups);
        Assert.Equal(groupId, loaded.DesignMetadata.Groups[0].Id);
        Assert.Equal("Test Group", loaded.DesignMetadata.Groups[0].Name);
        Assert.Equal(["r1", "t1"], loaded.DesignMetadata.Groups[0].MemberIds);
        Assert.True(loaded.DesignMetadata.Groups[0].IsVisible);
        Assert.False(loaded.DesignMetadata.Groups[0].IsLocked);
    }

    private static void CreatePackage(string path, string documentJson)
    {
        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(DocumentPackage.DocumentEntry);
        using Stream entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(documentJson));
    }
}
