using LabelStudio.Document;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Snapping;

public static class SnapEngine
{
    public static SnapResult Snap(SnapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);

        if (!request.Enabled || request.Sources == SnapSources.None || request.Tolerance.Value < 0)
        {
            return Unsnapped(request.ProposedBounds);
        }

        List<Candidate> candidates = [];
        AddDocumentCandidates(request, candidates);
        AddGeometryCandidates(request, candidates);
        AddGuideCandidates(request, candidates);
        AddGridCandidates(request, candidates);

        Candidate? x = SelectCandidate(request, candidates, SnapAxis.X, request.PreviousResult?.XSnap);
        Candidate? y = SelectCandidate(request, candidates, SnapAxis.Y, request.PreviousResult?.YSnap);
        MicrometreRect bounds = Apply(request, x, y);

        AxisSnap? xSnap = x is null ? null : ToAxisSnap(x);
        AxisSnap? ySnap = y is null ? null : ToAxisSnap(y);
        List<SnapIndicator> indicators = [];
        if (x is not null)
        {
            indicators.Add(ToIndicator(x, bounds));
        }

        if (y is not null)
        {
            indicators.Add(ToIndicator(y, bounds));
        }

        return new(bounds, xSnap, ySnap, indicators.AsReadOnly());
    }

    private static void AddDocumentCandidates(SnapRequest request, List<Candidate> candidates)
    {
        MicrometreRect label = new(
            Micrometre.Zero,
            Micrometre.Zero,
            request.Document.PageDimensions.Width,
            request.Document.PageDimensions.Height);

        if (request.Sources.HasFlag(SnapSources.Label))
        {
            AddRectTarget(request, candidates, SnapSources.Label, null, label);
        }

        if (request.Sources.HasFlag(SnapSources.Printable))
        {
            AddRectTarget(request, candidates, SnapSources.Printable, null,
                DocumentPrintableGeometry.GetMediaPrintableArea(request.Document));
        }

        if (request.Sources.HasFlag(SnapSources.SafeMargin))
        {
            AddRectTarget(request, candidates, SnapSources.SafeMargin, null,
                DocumentPrintableGeometry.GetSafeArea(request.Document));
        }
    }

    private static void AddGeometryCandidates(SnapRequest request, List<Candidate> candidates)
    {
        if (!request.Sources.HasFlag(SnapSources.Geometry))
        {
            return;
        }

        HashSet<string> excluded = new(request.ExcludedElementIds, StringComparer.Ordinal);
        foreach (var element in request.Document.Elements)
        {
            if (request.Document.IsEffectivelyVisible(element) && !excluded.Contains(element.Id))
            {
                AddRectTarget(request, candidates, SnapSources.Geometry, element.Id, element.Bounds);
            }
        }
    }

    private static void AddGridCandidates(SnapRequest request, List<Candidate> candidates)
    {
        if (!request.Sources.HasFlag(SnapSources.Grid))
        {
            return;
        }

        DocumentGridGeometry grid = request.Document.DesignMetadata.Grid;
        AddGridAxis(request, candidates, SnapAxis.X, grid.Origin.X.Value, grid.XSpacing.Value);
        AddGridAxis(request, candidates, SnapAxis.Y, grid.Origin.Y.Value, grid.YSpacing.Value);
    }

    private static void AddGuideCandidates(SnapRequest request, List<Candidate> candidates)
    {
        if (!request.Sources.HasFlag(SnapSources.Guides))
        {
            return;
        }

        foreach (DocumentGuide guide in request.Document.DesignMetadata.Guides)
        {
            SnapAxis axis = guide.Orientation == DocumentGuideOrientation.Vertical
                ? SnapAxis.X
                : SnapAxis.Y;
            foreach ((SnapFeature feature, int position) in GetMovingFeatures(request, axis))
            {
                int spanEnd = axis == SnapAxis.X
                    ? request.Document.PageDimensions.Height.Value
                    : request.Document.PageDimensions.Width.Value;
                AddCandidate(request, candidates, axis, feature, position, guide.Position.Value,
                    SnapSources.Guides, SnapFeature.Guide, guide.Id, 0, spanEnd);
            }
        }
    }

    private static void AddGridAxis(
        SnapRequest request,
        List<Candidate> candidates,
        SnapAxis axis,
        int origin,
        int spacing)
    {
        if (spacing <= 0)
        {
            return;
        }

        foreach ((SnapFeature feature, int position) in GetMovingFeatures(request, axis))
        {
            int target = origin + (int)Math.Round(
                (double)(position - origin) / spacing,
                MidpointRounding.AwayFromZero) * spacing;
            AddCandidate(request, candidates, axis, feature, position, target, SnapSources.Grid,
                SnapFeature.GridLine, null, PerpendicularStart(request.ProposedBounds, axis),
                PerpendicularEnd(request.ProposedBounds, axis));

            AxisSnap? previous = axis == SnapAxis.X
                ? request.PreviousResult?.XSnap
                : request.PreviousResult?.YSnap;
            if (previous is not null &&
                previous.Source.Source == SnapSources.Grid &&
                previous.MovingFeature == feature &&
                previous.TargetPosition.Value != target)
            {
                AddCandidate(request, candidates, axis, feature, position, previous.TargetPosition.Value,
                    SnapSources.Grid, SnapFeature.GridLine, null,
                    PerpendicularStart(request.ProposedBounds, axis),
                    PerpendicularEnd(request.ProposedBounds, axis));
            }
        }
    }

    private static void AddRectTarget(
        SnapRequest request,
        List<Candidate> candidates,
        SnapSources source,
        string? targetId,
        MicrometreRect target)
    {
        AddRectAxis(request, candidates, SnapAxis.X, source, targetId, target.X.Value,
            target.CentreX.Value, target.Right.Value, target.Y.Value, target.Bottom.Value);
        AddRectAxis(request, candidates, SnapAxis.Y, source, targetId, target.Y.Value,
            target.CentreY.Value, target.Bottom.Value, target.X.Value, target.Right.Value);
    }

    private static void AddRectAxis(
        SnapRequest request,
        List<Candidate> candidates,
        SnapAxis axis,
        SnapSources source,
        string? targetId,
        int start,
        int centre,
        int end,
        int spanStart,
        int spanEnd)
    {
        foreach ((SnapFeature movingFeature, int movingPosition) in GetMovingFeatures(request, axis))
        {
            AddCandidate(request, candidates, axis, movingFeature, movingPosition, start,
                source, SnapFeature.Start, targetId, spanStart, spanEnd);
            AddCandidate(request, candidates, axis, movingFeature, movingPosition, centre,
                source, SnapFeature.Centre, targetId, spanStart, spanEnd);
            AddCandidate(request, candidates, axis, movingFeature, movingPosition, end,
                source, SnapFeature.End, targetId, spanStart, spanEnd);
        }
    }

    private static IEnumerable<(SnapFeature Feature, int Position)> GetMovingFeatures(
        SnapRequest request,
        SnapAxis axis)
    {
        MicrometreRect bounds = request.ProposedBounds;
        if (request.Operation == SnapOperation.Move)
        {
            if (axis == SnapAxis.X)
            {
                yield return (SnapFeature.Start, bounds.X.Value);
                yield return (SnapFeature.Centre, bounds.CentreX.Value);
                yield return (SnapFeature.End, bounds.Right.Value);
            }
            else
            {
                yield return (SnapFeature.Start, bounds.Y.Value);
                yield return (SnapFeature.Centre, bounds.CentreY.Value);
                yield return (SnapFeature.End, bounds.Bottom.Value);
            }

            yield break;
        }

        if (axis == SnapAxis.X)
        {
            if (request.ResizeEdges.HasFlag(SnapEdges.Left))
                yield return (SnapFeature.Start, bounds.X.Value);
            if (request.ResizeEdges.HasFlag(SnapEdges.Right))
                yield return (SnapFeature.End, bounds.Right.Value);
        }
        else
        {
            if (request.ResizeEdges.HasFlag(SnapEdges.Top))
                yield return (SnapFeature.Start, bounds.Y.Value);
            if (request.ResizeEdges.HasFlag(SnapEdges.Bottom))
                yield return (SnapFeature.End, bounds.Bottom.Value);
        }
    }

    private static void AddCandidate(
        SnapRequest request,
        List<Candidate> candidates,
        SnapAxis axis,
        SnapFeature movingFeature,
        int movingPosition,
        int targetPosition,
        SnapSources source,
        SnapFeature targetFeature,
        string? targetId,
        int spanStart,
        int spanEnd)
    {
        long delta = (long)targetPosition - movingPosition;
        long limit = (long)request.Tolerance.Value + Math.Max(0, request.StickinessTolerance.Value);
        if (Math.Abs(delta) > limit || delta is < int.MinValue or > int.MaxValue)
        {
            return;
        }

        if (request.Operation == SnapOperation.Resize)
        {
            int size = axis == SnapAxis.X
                ? request.ProposedBounds.Width.Value
                : request.ProposedBounds.Height.Value;
            int resized = movingFeature == SnapFeature.Start ? size - (int)delta : size + (int)delta;
            if (resized < 0)
            {
                return;
            }
        }

        candidates.Add(new(axis, new((int)delta), movingFeature, new(targetPosition),
            new(source, targetFeature, targetId), new(spanStart), new(spanEnd)));
    }

    private static Candidate? SelectCandidate(
        SnapRequest request,
        List<Candidate> candidates,
        SnapAxis axis,
        AxisSnap? previous)
    {
        if (request.StickinessTolerance.Value > 0 && previous is not null)
        {
            Candidate? sticky = candidates
                .Where(candidate => candidate.Axis == axis && Matches(candidate, previous))
                .OrderBy(candidate => Math.Abs((long)candidate.Delta.Value))
                .ThenBy(CandidateOrder)
                .FirstOrDefault();
            if (sticky is not null)
            {
                return sticky;
            }
        }

        return candidates
            .Where(candidate => candidate.Axis == axis &&
                Math.Abs((long)candidate.Delta.Value) <= request.Tolerance.Value)
            .OrderBy(candidate => Math.Abs((long)candidate.Delta.Value))
            .ThenByDescending(candidate => Priority(candidate.Source.Source))
            .ThenBy(CandidateOrder)
            .FirstOrDefault();
    }

    private static bool Matches(Candidate candidate, AxisSnap previous) =>
        candidate.MovingFeature == previous.MovingFeature &&
        candidate.TargetPosition == previous.TargetPosition &&
        candidate.Source == previous.Source;

    private static string CandidateOrder(Candidate candidate) =>
        $"{candidate.TargetPosition.Value:D11}:{(int)candidate.MovingFeature}:{candidate.Source.TargetId}";

    private static int Priority(SnapSources source) => source switch
    {
        SnapSources.SafeMargin => 5,
        SnapSources.Printable => 4,
        SnapSources.Label => 3,
        SnapSources.Guides => 2,
        SnapSources.Geometry => 2,
        SnapSources.Grid => 1,
        _ => 0,
    };

    private static MicrometreRect Apply(SnapRequest request, Candidate? x, Candidate? y)
    {
        MicrometreRect bounds = request.ProposedBounds;
        if (request.Operation == SnapOperation.Move)
        {
            return bounds.Offset(x?.Delta ?? Micrometre.Zero, y?.Delta ?? Micrometre.Zero);
        }

        int left = bounds.X.Value;
        int top = bounds.Y.Value;
        int width = bounds.Width.Value;
        int height = bounds.Height.Value;
        if (x is not null)
        {
            if (x.MovingFeature == SnapFeature.Start)
            {
                left += x.Delta.Value;
                width -= x.Delta.Value;
            }
            else
            {
                width += x.Delta.Value;
            }
        }

        if (y is not null)
        {
            if (y.MovingFeature == SnapFeature.Start)
            {
                top += y.Delta.Value;
                height -= y.Delta.Value;
            }
            else
            {
                height += y.Delta.Value;
            }
        }

        return new(new(left), new(top), new(width), new(height));
    }

    private static AxisSnap ToAxisSnap(Candidate candidate) =>
        new(candidate.Axis, candidate.Delta, candidate.MovingFeature,
            candidate.TargetPosition, candidate.Source);

    private static SnapIndicator ToIndicator(Candidate candidate, MicrometreRect bounds)
    {
        int movingStart = PerpendicularStart(bounds, candidate.Axis);
        int movingEnd = PerpendicularEnd(bounds, candidate.Axis);
        return new(candidate.Axis, candidate.TargetPosition,
            new(Math.Min(candidate.SpanStart.Value, movingStart)),
            new(Math.Max(candidate.SpanEnd.Value, movingEnd)),
            candidate.MovingFeature, candidate.Source);
    }

    private static int PerpendicularStart(MicrometreRect bounds, SnapAxis axis) =>
        axis == SnapAxis.X ? bounds.Y.Value : bounds.X.Value;

    private static int PerpendicularEnd(MicrometreRect bounds, SnapAxis axis) =>
        axis == SnapAxis.X ? bounds.Bottom.Value : bounds.Right.Value;

    private static SnapResult Unsnapped(MicrometreRect bounds) =>
        new(bounds, null, null, Array.Empty<SnapIndicator>());

    private sealed record Candidate(
        SnapAxis Axis,
        Micrometre Delta,
        SnapFeature MovingFeature,
        Micrometre TargetPosition,
        SnapSourceMetadata Source,
        Micrometre SpanStart,
        Micrometre SpanEnd);
}
