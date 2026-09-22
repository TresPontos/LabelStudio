using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Snapping;

namespace LabelStudio.Editor.Tests;

public class SnapEngineTests
{
    [Fact]
    public void Move_SnapsAxesIndependentlyToPeerEdgesAndCentres()
    {
        LabelDocument document = CreateDocument(Rect("target", 10_000, 20_000, 4_000, 6_000));
        SnapRequest request = Move(document, Bounds(6_100, 21_950, 4_000, 2_000), 150,
            SnapSources.Geometry);

        SnapResult result = SnapEngine.Snap(request);

        Assert.Equal(6_000, result.Bounds.X.Value);
        Assert.Equal(22_000, result.Bounds.Y.Value);
        Assert.Equal(SnapFeature.End, result.XSnap!.MovingFeature);
        Assert.Equal(SnapFeature.Start, result.XSnap.Source.Feature);
        Assert.Equal("target", result.XSnap.Source.TargetId);
        Assert.Equal(SnapFeature.Centre, result.YSnap!.MovingFeature);
        Assert.Equal(SnapFeature.Centre, result.YSnap.Source.Feature);
    }

    [Fact]
    public void Geometry_IgnoresHiddenAndExcludedButIncludesLockedTargets()
    {
        LabelDocument document = CreateDocument(
            Rect("hidden", 10_000, 0, 1_000, 1_000) with { IsVisible = false },
            Rect("moving", 20_000, 0, 1_000, 1_000),
            Rect("locked", 30_000, 0, 1_000, 1_000) with { IsLocked = true });

        SnapResult hidden = SnapEngine.Snap(Move(document, Bounds(9_900, 5_000, 1_000, 1_000), 200,
            SnapSources.Geometry));
        SnapResult excluded = SnapEngine.Snap(Move(document, Bounds(19_900, 5_000, 1_000, 1_000), 200,
            SnapSources.Geometry) with
        { ExcludedElementIds = ["moving"] });
        SnapResult locked = SnapEngine.Snap(Move(document, Bounds(28_900, 5_000, 1_000, 1_000), 200,
            SnapSources.Geometry));

        Assert.Null(hidden.XSnap);
        Assert.Null(excluded.XSnap);
        Assert.Equal("locked", locked.XSnap!.Source.TargetId);
        Assert.Equal(29_000, locked.Bounds.X.Value);
    }

    [Fact]
    public void Geometry_IgnoresTargetsInHiddenGroups()
    {
        RectangleElement target = Rect("target", 10_000, 0, 1_000, 1_000);
        PhysicalSize dimensions = PhysicalSize.FromMillimetres(62, 30);
        DocumentDesignMetadata metadata = new(
            [new ElementGroup("group", null, [target.Id], isVisible: false)],
            [],
            DocumentGridGeometry.Default);
        LabelDocument document = LabelDocument.Create(
            dimensions,
            "test",
            new MediaSnapshot("test", dimensions, Bounds(6_000, 3_000, 50_000, 24_000)),
            [target],
            designMetadata: metadata);

        SnapResult result = SnapEngine.Snap(Move(document, Bounds(9_950, 5_000, 1_000, 1_000), 100,
            SnapSources.Geometry));

        Assert.Null(result.XSnap);
    }

    [Fact]
    public void Label_SnapsEdgesAndCentres()
    {
        LabelDocument document = CreateDocument();

        SnapResult edge = SnapEngine.Snap(Move(document, Bounds(51_950, 1_000, 10_000, 2_000), 100,
            SnapSources.Label));
        SnapResult centre = SnapEngine.Snap(Move(document, Bounds(26_050, 1_000, 10_000, 2_000), 100,
            SnapSources.Label));

        Assert.Equal(52_000, edge.Bounds.X.Value);
        Assert.Equal(SnapFeature.End, edge.XSnap!.Source.Feature);
        Assert.Equal(26_000, centre.Bounds.X.Value);
        Assert.Equal(SnapFeature.Centre, centre.XSnap!.Source.Feature);
    }

    [Fact]
    public void PrintableAndSafeMargin_SnapEdgesAndCentres()
    {
        LabelDocument document = CreateDocument();
        SnapResult printable = SnapEngine.Snap(Move(document, Bounds(5_900, 9_000, 4_000, 2_000), 150,
            SnapSources.Printable));
        SnapResult safeCentre = SnapEngine.Snap(Move(document, Bounds(28_050, 9_000, 6_000, 2_000), 100,
            SnapSources.SafeMargin));

        Assert.Equal(6_000, printable.Bounds.X.Value);
        Assert.Equal(SnapSources.Printable, printable.XSnap!.Source.Source);
        Assert.Equal(28_000, safeCentre.Bounds.X.Value);
        Assert.Equal(SnapFeature.Centre, safeCentre.XSnap!.Source.Feature);
    }

    [Fact]
    public void Grid_UsesDocumentGridOriginAndIndependentSpacing()
    {
        LabelDocument document = CreateDocumentWithGrid(new(
            new(1_000), new(2_000), new(new(250), new(500)), 5));

        SnapResult result = SnapEngine.Snap(Move(document, Bounds(3_210, 4_460, 500, 500), 50,
            SnapSources.Grid));

        Assert.Equal(3_250, result.Bounds.X.Value);
        Assert.Equal(4_500, result.Bounds.Y.Value);
        Assert.Equal(SnapFeature.GridLine, result.XSnap!.Source.Feature);
    }

    [Fact]
    public void Guides_SnapOnTheirDeclaredAxis()
    {
        PhysicalSize dimensions = PhysicalSize.FromMillimetres(62, 30);
        DocumentDesignMetadata metadata = new(
            [],
            [
                new DocumentGuide("vertical", DocumentGuideOrientation.Vertical, new(12_000)),
                new DocumentGuide("horizontal", DocumentGuideOrientation.Horizontal, new(8_000)),
            ],
            DocumentGridGeometry.Default);
        LabelDocument document = LabelDocument.Create(
            dimensions,
            "test",
            new MediaSnapshot("test", dimensions, Bounds(6_000, 3_000, 50_000, 24_000)),
            designMetadata: metadata);

        SnapResult result = SnapEngine.Snap(Move(document, Bounds(11_950, 7_950, 1_000, 1_000), 100,
            SnapSources.Guides));

        Assert.Equal(12_000, result.Bounds.X.Value);
        Assert.Equal(8_000, result.Bounds.Y.Value);
        Assert.Equal("vertical", result.XSnap!.Source.TargetId);
        Assert.Equal(SnapFeature.Guide, result.YSnap!.Source.Feature);
    }

    [Fact]
    public void PerSourceDisabled_OnlyEnabledSourcesParticipate()
    {
        LabelDocument document = CreateDocument(Rect("target", 10_000, 0, 1_000, 1_000));
        SnapRequest request = Move(document, Bounds(9_950, 5_000, 1_000, 1_000), 100,
            SnapSources.Label);

        SnapResult result = SnapEngine.Snap(request);

        Assert.Null(result.XSnap);
        Assert.Equal(request.ProposedBounds, result.Bounds);
    }

    [Fact]
    public void DisabledRequest_RepresentsGlobalOrAltBypass()
    {
        LabelDocument document = CreateDocument(Rect("target", 10_000, 0, 1_000, 1_000));
        SnapRequest request = Move(document, Bounds(9_950, 0, 1_000, 1_000), 100,
            SnapSources.All) with
        { Enabled = false };

        SnapResult result = SnapEngine.Snap(request);

        Assert.False(result.IsSnapped);
        Assert.Equal(request.ProposedBounds, result.Bounds);
        Assert.Empty(result.Indicators);
    }

    [Fact]
    public void NearestDistanceWinsBeforeSourcePriority()
    {
        LabelDocument document = CreateDocumentWithSafeMargins(
            DocumentSafeMargins.Uniform(new(10_000)),
            Rect("peer", 15_980, 0, 1_000, 1_000));
        SnapResult result = SnapEngine.Snap(Move(document, Bounds(15_950, 5_000, 1_000, 1_000), 100,
            SnapSources.Geometry | SnapSources.SafeMargin));

        Assert.Equal(SnapSources.Geometry, result.XSnap!.Source.Source);
        Assert.Equal(15_980, result.Bounds.X.Value);
    }

    [Fact]
    public void EqualDistanceUsesSpecifiedSourcePriority()
    {
        LabelDocument document = CreateDocumentWithSafeMargins(
            DocumentSafeMargins.Uniform(new(10_000)),
            Rect("peer", 16_000, 0, 1_000, 1_000));
        SnapResult result = SnapEngine.Snap(Move(document, Bounds(15_950, 5_000, 1_000, 1_000), 100,
            SnapSources.Geometry | SnapSources.SafeMargin));

        Assert.Equal(SnapSources.SafeMargin, result.XSnap!.Source.Source);
    }

    [Theory]
    [InlineData(SnapSources.All, SnapSources.SafeMargin)]
    [InlineData(SnapSources.Printable | SnapSources.Label | SnapSources.Geometry | SnapSources.Grid, SnapSources.Printable)]
    [InlineData(SnapSources.Label | SnapSources.Geometry | SnapSources.Grid, SnapSources.Label)]
    [InlineData(SnapSources.Geometry | SnapSources.Grid, SnapSources.Geometry)]
    [InlineData(SnapSources.Grid, SnapSources.Grid)]
    public void EqualDistance_UsesCompletePriorityOrder(SnapSources sources, SnapSources expected)
    {
        PhysicalSize dimensions = PhysicalSize.FromMillimetres(62, 30);
        DocumentDesignMetadata metadata = new(
            [], [], DocumentGridGeometry.Default, DocumentSafeMargins.Uniform(Micrometre.Zero));
        LabelDocument document = LabelDocument.Create(
            dimensions,
            "test",
            new MediaSnapshot("test", dimensions, Bounds(0, 0, 62_000, 30_000)),
            [Rect("peer", 0, 5_000, 500, 500)],
            designMetadata: metadata);

        SnapResult result = SnapEngine.Snap(Move(document, Bounds(50, 10_000, 500, 500), 100, sources));

        Assert.Equal(expected, result.XSnap!.Source.Source);
    }

    [Fact]
    public void Resize_SnapsOnlyActiveEdgesAndKeepsOppositeEdgesFixed()
    {
        LabelDocument document = CreateDocument(Rect("target", 10_000, 20_000, 2_000, 2_000));
        SnapRequest request = new(document, Bounds(9_900, 15_000, 5_100, 4_900),
            SnapOperation.Resize, new(150))
        {
            Sources = SnapSources.Geometry,
            ResizeEdges = SnapEdges.Left | SnapEdges.Bottom,
        };

        SnapResult result = SnapEngine.Snap(request);

        Assert.Equal(10_000, result.Bounds.X.Value);
        Assert.Equal(5_000, result.Bounds.Width.Value);
        Assert.Equal(20_000, result.Bounds.Bottom.Value);
        Assert.Equal(15_000, result.Bounds.Y.Value);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    [InlineData(12.0)]
    public void ScreenTolerance_IsConvertedByCanvasTransformBeforeRequest(double zoom)
    {
        CanvasTransform transform = new() { Zoom = zoom };
        Micrometre tolerance = transform.ScreenToleranceToDocument(6);
        LabelDocument document = CreateDocument(Rect("target", 20_000, 0, 1_000, 1_000));
        int within = Math.Max(1, tolerance.Value - 1);

        SnapResult result = SnapEngine.Snap(Move(document,
            Bounds(20_000 - within, 5_000, 0, 1_000), tolerance.Value,
            SnapSources.Geometry));

        Assert.Equal(20_000, result.Bounds.X.Value);
        Assert.Equal(within, result.XSnap!.Delta.Value);
    }

    [Fact]
    public void Result_ContainsDocumentSpaceIndicatorAndSemanticMetadata()
    {
        LabelDocument document = CreateDocument(Rect("peer", 10_000, 20_000, 4_000, 6_000));
        SnapResult result = SnapEngine.Snap(Move(document, Bounds(6_050, 21_000, 4_000, 2_000), 100,
            SnapSources.Geometry));

        SnapIndicator indicator = Assert.Single(result.Indicators, i => i.Axis == SnapAxis.X);
        Assert.Equal(10_000, indicator.Position.Value);
        Assert.Equal(20_000, indicator.SpanStart.Value);
        Assert.Equal(26_000, indicator.SpanEnd.Value);
        Assert.Equal("peer", indicator.Source.TargetId);
        Assert.Equal(SnapSources.Geometry, indicator.Source.Source);
    }

    [Fact]
    public void PreviousResult_CanHoldSnapWithinStickinessTolerance()
    {
        LabelDocument document = CreateDocument(Rect("peer", 10_000, 0, 1_000, 1_000));
        SnapResult first = SnapEngine.Snap(Move(document, Bounds(9_950, 5_000, 1_000, 1_000), 100,
            SnapSources.Geometry));
        SnapRequest next = Move(document, Bounds(9_850, 5_000, 1_000, 1_000), 100,
            SnapSources.Geometry) with
        {
            PreviousResult = first,
            StickinessTolerance = new(100),
        };

        SnapResult result = SnapEngine.Snap(next);

        Assert.Equal(10_000, result.Bounds.X.Value);
        Assert.Equal("peer", result.XSnap!.Source.TargetId);
    }

    private static SnapRequest Move(
        LabelDocument document,
        MicrometreRect bounds,
        int tolerance,
        SnapSources sources) =>
        new(document, bounds, SnapOperation.Move, new(tolerance)) { Sources = sources };

    private static LabelDocument CreateDocument(params DocumentElement[] elements) =>
        CreateDocument(DocumentGridGeometry.Default, DocumentSafeMargins.Uniform(new(2_000)), elements);

    private static LabelDocument CreateDocumentWithGrid(DocumentGridGeometry grid) =>
        CreateDocument(grid, DocumentSafeMargins.Uniform(new(2_000)));

    private static LabelDocument CreateDocumentWithSafeMargins(
        DocumentSafeMargins safeMargins,
        params DocumentElement[] elements) =>
        CreateDocument(DocumentGridGeometry.Default, safeMargins, elements);

    private static LabelDocument CreateDocument(
        DocumentGridGeometry grid,
        DocumentSafeMargins safeMargins,
        params DocumentElement[] elements)
    {
        PhysicalSize dimensions = PhysicalSize.FromMillimetres(62, 30);
        MediaSnapshot media = new("test", dimensions, Bounds(6_000, 3_000, 50_000, 24_000));
        DocumentDesignMetadata metadata = new([], [], grid, safeMargins);
        return LabelDocument.Create(dimensions, "test", media, elements, designMetadata: metadata);
    }

    private static RectangleElement Rect(string id, int x, int y, int width, int height) =>
        RectangleElement.Create(id, Bounds(x, y, width, height), InkChannel.Black);

    private static MicrometreRect Bounds(int x, int y, int width, int height) =>
        new(new(x), new(y), new(width), new(height));
}
