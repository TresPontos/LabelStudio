using LabelStudio.Diagnostics;
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

namespace LabelStudio.DevCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "create" => CreateSample(args[1..]),
            "inspect" => Inspect(args[1..]),
            "render" => Render(args[1..]),
            "mock-print" => MockPrint(args[1..]),
            "print" => Print(args[1..]),
            _ => UsageWithError($"Unknown command: {args[0]}"),
        };
    }

    private static int CreateSample(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "sample.label";
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 48.26);
        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.Dk22251();
        Micrometre feedMargin = new(PhysicalUnits.DotsToMicrometres(
            mapping.MinimumFeedMarginDots,
            QlContinuousLengthPlanner.Dpi));
        MediaSnapshot snap = new("brother.dk-22251", dims,
            new MicrometreRect(
                new(1500),
                feedMargin,
                new(58_900),
                new(dims.Height.Value - (2 * feedMargin.Value))));

        List<DocumentElement> elements =
        [
            RectangleElement.Create("rect-1",
                new(new(2000), new(2000), new(20000), new(10000)), InkChannel.Black, fill: true),
            new LineElement("line-1",
                new(new(2000), new(15000)),
                new(new(55000), new(15000)),
                new(400), InkChannel.Black),
            new TextElement("text-1",
                new(new(2000), new(18000), new(40000), new(4000)),
                InkChannel.Black, "LABEL STUDIO", 24, null),
        ];

        LabelDocument doc = LabelDocument.Create(
            dims, "brother.dk-22251", snap, elements, mediaKind: DocumentMediaKind.Continuous);
        DocumentPackage.Save(doc, path);
        Console.WriteLine($"Created: {Path.GetFullPath(path)}");
        Console.WriteLine($"  Id: {doc.Id}");
        Console.WriteLine($"  Elements: {doc.Elements.Count}");
        Console.WriteLine($"  Media: {doc.MediaProfileId}");
        return 0;
    }

    private static int Inspect(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "sample.label";
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        LabelDocument doc = DocumentPackage.Load(path);
        Console.WriteLine($"Document: {path}");
        Console.WriteLine($"  Id: {doc.Id}");
        Console.WriteLine($"  Version: {doc.FormatVersion}");
        Console.WriteLine($"  Page: {doc.PageDimensions.Width.ToMillimetres():F1} x {doc.PageDimensions.Height.ToMillimetres():F1} mm");
        Console.WriteLine($"  Media: {doc.MediaProfileId}");
        Console.WriteLine($"  Elements: {doc.Elements.Count}");
        foreach (DocumentElement element in doc.Elements)
        {
            Console.WriteLine($"    [{element.ElementType}] {element.Id} ink={element.Ink} bounds=({element.Bounds.X},{element.Bounds.Y},{element.Bounds.Width},{element.Bounds.Height})");
        }
        Console.WriteLine($"  Print: ink={doc.PrintDefaults.DefaultInk} dpi={doc.PrintDefaults.Dpi} autoCut={doc.PrintDefaults.AutoCut}");
        return 0;
    }

    private static int Render(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "sample.label";
        string outputDir = args.Length > 1 ? args[1] : "render-output";

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        LabelDocument doc = DocumentPackage.Load(path);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(doc.MediaProfileId);
        RenderTarget target = CreateRenderTarget(doc, media, doc.PrintDefaults.Dpi);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        RasterDump.Dump(planes, outputDir);

        Console.WriteLine($"Rendered: {Path.GetFullPath(outputDir)}");
        Console.WriteLine($"  Black plane: {planes.BlackPlane.Width} x {planes.BlackPlane.Height}");
        Console.WriteLine($"  Red plane: {(planes.RedPlane is null ? "none" : $"{planes.RedPlane.Width} x {planes.RedPlane.Height}")}");
        return 0;
    }

    private static int MockPrint(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "sample.label";
        string outputDir = args.Length > 1 ? args[1] : "mock-output";

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        LabelDocument doc = DocumentPackage.Load(path);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(doc.MediaProfileId);

        PrintSettings settings = new(doc.PrintDefaults.DefaultInk, doc.PrintDefaults.AutoCut,
            doc.PrintDefaults.CutAtEnd, doc.PrintDefaults.Dpi);

        PreflightResult preflight = new PreflightEngine().Check(doc, scene, media, settings);
        if (!preflight.CanPrint)
        {
            Console.Error.WriteLine("Preflight FAILED:");
            foreach (PreflightIssue issue in preflight.Errors)
            {
                Console.Error.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
            }
            return 1;
        }

        RenderTarget target = CreateRenderTarget(doc, media, settings.Dpi);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        PrintIntent intent = new(doc.Id, scene, media, settings);
        DevicePrintJob job = new(intent, planes, media, settings, $"Mock-{doc.Id}");

        MockFileBackend backend = new(outputDir);
        PrintResult result = backend.Print(job);

        Console.WriteLine(result.Success
            ? $"Mock print succeeded: {Path.GetFullPath(outputDir)}"
            : $"Mock print FAILED: {result.Error}");
        return result.Success ? 0 : 1;
    }

    private static RenderTarget CreateRenderTarget(LabelDocument document, MediaProfile media, int dpi)
    {
        if (dpi != QlContinuousLengthPlanner.Dpi)
        {
            throw new ArgumentException(
                $"QL rendering requires {QlContinuousLengthPlanner.Dpi} DPI, got {dpi} DPI.",
                nameof(dpi));
        }

        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(media.ProfileId);
        int rasterHeight = media.Kind == MediaKind.DieCut
            ? mapping.PrintableLengthDots
            : QlContinuousLengthPlanner.Plan(document.PageDimensions.Height, mapping).RasterRows;
        MicrometreRect printable = DocumentPrintableGeometry.GetMediaPrintableArea(document);
        return new RenderTarget(
            dpi,
            dpi,
            720,
            rasterHeight,
            printable,
            mapping.HeadLeftBlankDots,
            mapping.PrintableWidthDots,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)])
        {
            DocumentOriginX = printable.X,
            DocumentOriginY = printable.Y,
        };
    }

    private static int Print(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "sample.label";
        string? printerQueue = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--printer" && i + 1 < args.Length)
            {
                printerQueue = args[++i];
            }
        }

        if (string.IsNullOrEmpty(printerQueue))
        {
            Console.Error.WriteLine("Physical printing requires --printer <queue-name>.");
            Console.Error.WriteLine("No physical printing will occur without explicit opt-in.");
            return 1;
        }

        Console.Error.WriteLine($"Physical printing to '{printerQueue}' is not yet wired in this build.");
        Console.Error.WriteLine("Use the Ql800Spike.Cli for validated physical printing.");
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("LabelStudio Dev CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  create [path]                Create a sample .label document");
        Console.WriteLine("  inspect <path>                Inspect a .label document");
        Console.WriteLine("  render <path> [output]        Render to raster dump");
        Console.WriteLine("  mock-print <path> [output]    Mock print to file");
        Console.WriteLine("  print <path> --printer <q>   Physical print (requires explicit --printer)");
    }

    private static int UsageWithError(string error)
    {
        Console.Error.WriteLine(error);
        PrintUsage();
        return 1;
    }
}
