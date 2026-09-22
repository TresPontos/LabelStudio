using LabelStudio.Document;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Snapping;

[Flags]
public enum SnapSources
{
    None = 0,
    Geometry = 1 << 0,
    Label = 1 << 1,
    Printable = 1 << 2,
    SafeMargin = 1 << 3,
    Grid = 1 << 4,
    Guides = 1 << 5,
    All = Geometry | Label | Printable | SafeMargin | Grid | Guides,
}

public enum SnapOperation
{
    Move,
    Resize,
}

[Flags]
public enum SnapEdges
{
    None = 0,
    Left = 1 << 0,
    Top = 1 << 1,
    Right = 1 << 2,
    Bottom = 1 << 3,
    All = Left | Top | Right | Bottom,
}

public enum SnapAxis
{
    X,
    Y,
}

public enum SnapFeature
{
    Start,
    Centre,
    End,
    GridLine,
    Guide,
}

public sealed record SnapSourceMetadata(
    SnapSources Source,
    SnapFeature Feature,
    string? TargetId = null);

public sealed record AxisSnap(
    SnapAxis Axis,
    Micrometre Delta,
    SnapFeature MovingFeature,
    Micrometre TargetPosition,
    SnapSourceMetadata Source);

public sealed record SnapIndicator(
    SnapAxis Axis,
    Micrometre Position,
    Micrometre SpanStart,
    Micrometre SpanEnd,
    SnapFeature MovingFeature,
    SnapSourceMetadata Source);

public sealed record SnapResult(
    MicrometreRect Bounds,
    AxisSnap? XSnap,
    AxisSnap? YSnap,
    IReadOnlyList<SnapIndicator> Indicators)
{
    public bool IsSnapped => XSnap is not null || YSnap is not null;
}

public sealed record SnapRequest(
    LabelDocument Document,
    MicrometreRect ProposedBounds,
    SnapOperation Operation,
    Micrometre Tolerance)
{
    public bool Enabled { get; init; } = true;

    public SnapSources Sources { get; init; } = SnapSources.All;

    public SnapEdges ResizeEdges { get; init; } = SnapEdges.All;

    public IReadOnlyCollection<string> ExcludedElementIds { get; init; } = Array.Empty<string>();

    public SnapResult? PreviousResult { get; init; }

    public Micrometre StickinessTolerance { get; init; } = Micrometre.Zero;
}
