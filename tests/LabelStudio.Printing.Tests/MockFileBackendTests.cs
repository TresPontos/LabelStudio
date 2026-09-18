using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using LabelStudio.Printing;
using LabelStudio.Printing.Mock;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.Tests;

public class MockFileBackendTests
{
    private static string GetTempDir() =>
        Path.Combine(Path.GetTempPath(), "LabelStudioTests", Guid.NewGuid().ToString("N"));

    private static DevicePrintJob CreateJob()
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 0);
        MediaSnapshot snap = new("brother.dk-22251", dims,
            new MicrometreRect(new(1500), new(0), new(58900), new(0)));
        RectangleElement rect = RectangleElement.Create("r1",
            new(new(2000), new(2000), new(20000), new(10000)));
        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-22251", snap, [rect]);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");
        PrintSettings settings = PrintSettings.Default;

        RenderTarget target = new(300, 300, 720, 500,
            snap.PrintableArea, 12, 696,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", true)]);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        PrintIntent intent = new(doc.Id, scene, media, settings);
        return new DevicePrintJob(intent, planes, media, settings, "test-job");
    }

    [Fact]
    public void Print_WritesManifestAndPlanes()
    {
        string dir = GetTempDir();
        MockFileBackend backend = new(dir);
        DevicePrintJob job = CreateJob();

        PrintResult result = backend.Print(job);

        Assert.True(result.Success, result.Error);
        Assert.True(Directory.Exists(dir));

        string[] subDirs = Directory.GetDirectories(dir);
        Assert.Single(subDirs);

        string[] files = Directory.GetFiles(subDirs[0]);
        Assert.Contains(files, f => Path.GetFileName(f) == "manifest.json");
        Assert.Contains(files, f => Path.GetFileName(f) == "black-plane.png");
    }

    [Fact]
    public void Print_RejectsNullJob()
    {
        MockFileBackend backend = new(GetTempDir());
        Assert.Throws<ArgumentNullException>(() => backend.Print(null!));
    }
}