using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Tests;

public class ContinuousCutGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void CutSegmentAndPointerRecoverPhysicalLengthAtEveryViewRotation(int rotation)
    {
        CanvasTransform transform = new()
        {
            ViewRotationDegrees = rotation,
            Zoom = 2.25,
            OffsetX = 16,
            OffsetY = 16,
        };
        transform.SetContentSize(new(62_000), new(100_000));

        CanvasLineSegment segment = ContinuousCutGeometry.GetSegment(transform, new(62_000), new(84_000));
        CanvasPoint middle = new(
            (segment.Start.X + segment.End.X) / 2,
            (segment.Start.Y + segment.End.Y) / 2);

        Assert.Equal(new Micrometre(84_000),
            ContinuousCutGeometry.GetLengthFromCanvasPoint(transform, middle.X, middle.Y));
        double segmentLength = Math.Sqrt(
            Math.Pow(segment.End.X - segment.Start.X, 2) +
            Math.Pow(segment.End.Y - segment.Start.Y, 2));
        Assert.Equal(transform.DocumentToCanvasLength(new Micrometre(62_000)), segmentLength, 6);
    }

    [Fact]
    public void ChangingLengthMovesOnlyTheCutBoundary()
    {
        CanvasTransform transform = new() { ViewRotationDegrees = 90, Zoom = 2 };
        transform.SetContentSize(new(62_000), new(125_000));

        CanvasLineSegment first = ContinuousCutGeometry.GetSegment(transform, new(62_000), new(84_000));
        CanvasLineSegment second = ContinuousCutGeometry.GetSegment(transform, new(62_000), new(125_000));

        Assert.NotEqual(first.Start, second.Start);
        Assert.Equal(
            transform.DocumentToCanvasLength(new Micrometre(41_000)),
            Math.Abs(second.Start.X - first.Start.X),
            6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ResizePreviewCanPreserveDocumentOriginWhileCutBoundaryMoves(int rotation)
    {
        CanvasTransform transform = new()
        {
            ViewRotationDegrees = rotation,
            Zoom = 2,
            OffsetX = 16,
            OffsetY = 16,
        };
        transform.SetContentSize(new(62_000), new(84_000));
        (double originX, double originY) = transform.DocumentToCanvas(Micrometre.Zero, Micrometre.Zero);
        CanvasLineSegment before = ContinuousCutGeometry.GetSegment(transform, new(62_000), new(84_000));

        transform.SetContentSizePreservingDocumentOrigin(new(62_000), new(125_000));

        Assert.Equal((originX, originY), transform.DocumentToCanvas(Micrometre.Zero, Micrometre.Zero));
        CanvasLineSegment after = ContinuousCutGeometry.GetSegment(transform, new(62_000), new(125_000));
        Assert.NotEqual(before.Start, after.Start);
    }
}
