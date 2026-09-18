using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using LabelStudio.Printing;
using LabelStudio.Printing.BrotherQl;
using LabelStudio.Printing.Mock;
using LabelStudio.Rendering;
using LabelStudio.Storage;

namespace LabelStudio.Printing.Tests;

public class EndToEndTests
{
    private static string GetTempPath() =>
        Path.Combine(Path.GetTempPath(), "LabelStudioE2E", Guid.NewGuid().ToString("N") + DocumentPackage.Extension);

    private static LabelDocument CreateSampleDocument()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)));

        List<DocumentElement> elements =
        [
            RectangleElement.Create("rect",
                new(new(2000), new(2000), new(20000), new(10000)), InkChannel.Black, fill: true),
            new LineElement("line",
                new(new(2000), new(15000)),
                new(new(55000), new(15000)),
                new(400), InkChannel.Black),
            new TextElement("text",
                new(new(2000), new(18000), new(40000), new(4000)),
                InkChannel.Black, "TEST", 24, null),
        ];

        return LabelDocument.Create(dims, "brother.dk-22251", snap, elements);
    }

    [Fact]
    public void FullPipeline_Create_Save_Reopen_Render_Preflight_MockPrint()
    {
        string path = GetTempPath();
        string mockDir = Path.Combine(Path.GetTempPath(), "LabelStudioE2E", Guid.NewGuid().ToString("N"));

        LabelDocument doc = CreateSampleDocument();

        DocumentPackage.Save(doc, path);
        Assert.True(File.Exists(path));

        LabelDocument reopened = DocumentPackage.Load(path);
        Assert.Equal(doc.Id, reopened.Id);
        Assert.Equal(doc.Elements.Count, reopened.Elements.Count);

        PreparedScene scene = new LayoutEngine().Prepare(reopened);

        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(reopened.MediaProfileId);

        PrintSettings settings = PrintSettings.Default;

        PreflightResult preflight = new PreflightEngine().Check(reopened, scene, media, settings);
        Assert.True(preflight.CanPrint, string.Join("; ", preflight.Errors.Select(e => e.Message)));

        RenderTarget target = new(300, 300, 720, 500,
            reopened.MediaGeometry.PrintableArea, 12, 696,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)]);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        Assert.Equal(720, planes.BlackPlane.Width);

        PrintIntent intent = new(reopened.Id, scene, media, settings);
        DevicePrintJob job = new(intent, planes, media, settings, "e2e-test");

        MockFileBackend mock = new(mockDir);
        PrintResult result = mock.Print(job);
        Assert.True(result.Success, result.Error);

        string[] subDirs = Directory.GetDirectories(mockDir);
        Assert.Single(subDirs);
        string[] files = Directory.GetFiles(subDirs[0]);
        Assert.Contains(files, f => Path.GetFileName(f) == "manifest.json");
        Assert.Contains(files, f => Path.GetFileName(f) == "black-plane.png");
    }

    [Fact]
    public void FullPipeline_DK11204_DieCut()
    {
        PhysicalSize dims = new(new(17_000), new(53_900));
        MediaSnapshot snap = new("brother.dk-11204", dims,
            new MicrometreRect(new(1500), new(3000), new(14_000), new(47_900)));
        RectangleElement rect = RectangleElement.Create("r1",
            new(new(2000), new(4000), new(10000), new(30000)));
        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-11204", snap, [rect]);

        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");
        PrintSettings settings = PrintSettings.Default;

        PreflightResult preflight = new PreflightEngine().Check(doc, scene, media, settings);
        Assert.True(preflight.CanPrint, string.Join("; ", preflight.Errors.Select(e => e.Message)));

        RenderTarget target = new(300, 300, 720, 566,
            snap.PrintableArea, 555, 165,
            [new InkOutputChannel("Black", false)]);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        Assert.Equal(720, planes.BlackPlane.Width);
        Assert.Equal(566, planes.BlackPlane.Height);

        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk11204();
        QlRasterJobOptions qlOptions = new(media, mapping, planes.BlackPlane, null, 0);
        QlEncodedJob encoded = new QlRasterEncoder().Encode(qlOptions);

        QlValidationResult validation = new QlRasterValidator().Validate(encoded.Payload, media, mapping);
        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));
        Assert.Equal(566, validation.RasterLineCount);
        Assert.Equal(0, validation.FeedMarginDots);
        Assert.False(encoded.TwoColour);
    }

    [Fact]
    public void NoPhysicalPrint_FromTest()
    {
        Ql800PrinterBackend backend = new();
        Assert.Throws<ArgumentNullException>(() => backend.Print(null!));
    }
}