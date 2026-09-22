using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using LabelStudio.Printing;

namespace LabelStudio.Printing.Tests;

public class PreflightEngineTests
{
    private static (LabelDocument doc, PreparedScene scene) CreateContinuous(params DocumentElement[] elements)
    {
        PhysicalSize dims = PhysicalSize.FromMillimetres(62.0, 48.26);
        MediaSnapshot snap = new("brother.dk-22251", dims,
            new MicrometreRect(new(1500), new(0), new(58_900), new(0)));
        LabelDocument doc = LabelDocument.Create(
            dims, "brother.dk-22251", snap, elements, mediaKind: DocumentMediaKind.Continuous);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        return (doc, scene);
    }

    private static (LabelDocument doc, PreparedScene scene) CreateDieCut(params DocumentElement[] elements)
    {
        PhysicalSize dims = new(new(17_000), new(53_900));
        MediaSnapshot snap = new("brother.dk-11204", dims,
            new MicrometreRect(new(1500), new(3000), new(14_000), new(47_900)));
        LabelDocument doc = LabelDocument.Create(dims, "brother.dk-11204", snap, elements);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        return (doc, scene);
    }

    [Fact]
    public void ValidContinuousDocument_Passes()
    {
        var (doc, scene) = CreateContinuous();
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");
        PrintSettings settings = PrintSettings.Default;

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, settings);

        Assert.True(result.CanPrint);
    }

    [Fact]
    public void ValidDieCutDocument_Passes()
    {
        var (doc, scene) = CreateDieCut();
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");
        PrintSettings settings = PrintSettings.Default;

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, settings);

        Assert.True(result.CanPrint);
    }

    [Fact]
    public void RedInkOnBlackOnlyMedia_Fails()
    {
        RectangleElement redRect = RectangleElement.Create("r1",
            new(new(1000), new(1000), new(5000), new(5000)), InkChannel.Red);
        var (doc, scene) = CreateDieCut(redRect);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.False(result.CanPrint);
        Assert.Contains(result.Errors, e => e.Code == "INK_UNSUPPORTED");
    }

    [Fact]
    public void RedInkOnTwoColourMedia_Passes()
    {
        RectangleElement redRect = RectangleElement.Create("r1",
            new(new(1000), new(1000), new(5000), new(5000)), InkChannel.Red);
        var (doc, scene) = CreateContinuous(redRect);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.True(result.CanPrint);
    }

    [Fact]
    public void DimensionMismatch_Fails()
    {
        PhysicalSize wrongDims = PhysicalSize.FromMillimetres(50.0, 48.26);
        MediaSnapshot wrongSnap = new("brother.dk-22251", wrongDims, MicrometreRect.Zero);
        LabelDocument doc = LabelDocument.Create(
            wrongDims, "brother.dk-22251", wrongSnap, mediaKind: DocumentMediaKind.Continuous);
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.False(result.CanPrint);
        Assert.Contains(result.Errors, e => e.Code == "DIMENSION_MISMATCH");
    }

    [Fact]
    public void InvalidResolution_Fails()
    {
        var (doc, scene) = CreateContinuous();
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");
        PrintSettings badDpi = new(InkChannel.Black, true, true, 1200);

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, badDpi);

        Assert.False(result.CanPrint);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_RESOLUTION");
    }

    [Fact]
    public void AsymmetricDeviceResolutionCannotBeRepresentedByScalarDpi()
    {
        var (doc, scene) = CreateContinuous();
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");
        PrintSettings unsupported = new(InkChannel.Black, true, true, 600);

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, unsupported);

        Assert.Contains(result.Errors, e => e.Code == "INVALID_RESOLUTION");
    }

    [Fact]
    public void ElementOutsideLabelOrigin_Fails()
    {
        RectangleElement offscreen = RectangleElement.Create("r1",
            new(new(-1000), new(1000), new(5000), new(5000)));
        var (doc, scene) = CreateContinuous(offscreen);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-22251");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.False(result.CanPrint);
        Assert.Contains(result.Errors, e => e.Code == "ELEMENT_OUTSIDE_LABEL");
    }

    [Fact]
    public void HiddenRedElement_DoesNotFailInkPreflight()
    {
        RectangleElement hiddenRed = RectangleElement.Create(
            "hidden-red",
            new(new(1000), new(1000), new(5000), new(5000)),
            InkChannel.Red) with
        { IsVisible = false };
        var (doc, scene) = CreateDieCut(hiddenRed);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.True(result.CanPrint);
        Assert.DoesNotContain(result.Errors, issue => issue.ElementId == hiddenRed.Id);
    }

    [Fact]
    public void RotatedElement_PreflightUsesVisualBounds()
    {
        TextElement rotated = new(
            "rotated",
            new MicrometreRect(new(16_000), new(10_000), new(2_000), new(10_000)),
            InkChannel.Black,
            "Text",
            12,
            null)
        {
            RotationMillidegrees = 90_000,
        };
        var (doc, scene) = CreateDieCut(rotated);
        MediaProfile media = MediaCatalog.CreateBuiltIn().Get("brother.dk-11204");

        PreflightResult result = new PreflightEngine().Check(doc, scene, media, PrintSettings.Default);

        Assert.DoesNotContain(result.Errors, issue => issue.Code == "ELEMENT_OUTSIDE_PRINTABLE");
    }

    [Theory]
    [InlineData(12_699)]
    [InlineData(1_000_001)]
    public void ContinuousLengthOutsideCutterConstraints_Fails(int lengthMicrometres)
    {
        var (document, _) = CreateContinuous();
        document = document with
        {
            PageDimensions = new PhysicalSize(document.PageDimensions.Width, new Micrometre(lengthMicrometres)),
        };
        PreparedScene scene = new LayoutEngine().Prepare(document);

        PreflightResult result = new PreflightEngine().Check(
            document,
            scene,
            MediaCatalog.CreateBuiltIn().Get("brother.dk-22251"),
            PrintSettings.Default);

        Assert.Contains(result.Errors, issue => issue.Code == "CONTINUOUS_LENGTH");
    }

    [Fact]
    public void DocumentAndProfileMediaKindMismatch_Fails()
    {
        var (document, scene) = CreateContinuous();
        document = document with { MediaKind = DocumentMediaKind.DieCut };

        PreflightResult result = new PreflightEngine().Check(
            document,
            scene,
            MediaCatalog.CreateBuiltIn().Get("brother.dk-22251"),
            PrintSettings.Default);

        Assert.Contains(result.Errors, issue => issue.Code == "MEDIA_KIND_MISMATCH");
    }
}
