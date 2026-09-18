using System.IO.Compression;
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
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new(
            "brother.dk-22251",
            dims,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)));
        RectangleElement rect = RectangleElement.Create("r1",
            new(new(1000), new(1000), new(10000), new(5000)));
        LineElement line = new("l1",
            new(new(1000), new(7000)),
            new(new(11000), new(7000)),
            new(200), InkChannel.Black);
        TextElement text = new("t1",
            new(new(1000), new(8000), new(20000), new(3000)),
            InkChannel.Black, "Hello", 24, null);
        ImageElement img = new("i1",
            new(new(25000), new(1000), new(10000), new(10000)),
            InkChannel.Black, "asset-001");

        return LabelDocument.Create(dims, "brother.dk-22251", snap, [rect, line, text, img]);
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

        LineElement origLine = (LineElement)original.Elements[1];
        LineElement loadLine = (LineElement)loaded.Elements[1];
        Assert.Equal(origLine.Start, loadLine.Start);
        Assert.Equal(origLine.End, loadLine.End);
        Assert.Equal(origLine.Thickness, loadLine.Thickness);

        TextElement origText = (TextElement)original.Elements[2];
        TextElement loadText = (TextElement)loaded.Elements[2];
        Assert.Equal(origText.Text, loadText.Text);
        Assert.Equal(origText.FontSizePoints, loadText.FontSizePoints);

        ImageElement origImg = (ImageElement)original.Elements[3];
        ImageElement loadImg = (ImageElement)loaded.Elements[3];
        Assert.Equal(origImg.AssetId, loadImg.AssetId);
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
}