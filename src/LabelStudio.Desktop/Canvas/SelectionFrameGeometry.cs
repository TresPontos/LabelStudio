using System.Windows;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Transforms;

namespace LabelStudio.Desktop.Canvas;

internal sealed class SelectionFrameGeometry
{
    private static readonly ResizeHandle[] ResizeHandles =
    [
        ResizeHandle.TopLeft,
        ResizeHandle.Top,
        ResizeHandle.TopRight,
        ResizeHandle.Right,
        ResizeHandle.BottomRight,
        ResizeHandle.Bottom,
        ResizeHandle.BottomLeft,
        ResizeHandle.Left,
    ];

    public MicrometreRect Bounds { get; }
    public int RotationMillidegrees { get; }
    public IReadOnlyList<Point> Outline { get; }
    public IReadOnlyDictionary<ResizeHandle, Point> Handles { get; }
    public Point Center { get; }
    public Point RotationStem { get; }
    public Point RotationHandle { get; }

    private SelectionFrameGeometry(
        MicrometreRect bounds,
        int rotationMillidegrees,
        CanvasTransform view)
    {
        Bounds = bounds;
        RotationMillidegrees = rotationMillidegrees;
        Outline = ElementGeometry.GetRotatedCorners(bounds, rotationMillidegrees)
            .Select(point => ToScreen(view, point))
            .ToArray();
        Center = ToScreen(view, new GeometryPoint(
            bounds.X.Value + bounds.Width.Value / 2.0,
            bounds.Y.Value + bounds.Height.Value / 2.0));

        Dictionary<ResizeHandle, Point> handles = new();
        foreach (ResizeHandle handle in ResizeHandles)
        {
            handles[handle] = ToScreen(view, AnchoredResizeGesture.GetHandlePoint(
                bounds,
                rotationMillidegrees,
                ToResizeAnchor(handle)));
        }
        Handles = handles;

        RotationStem = handles[ResizeHandle.Top];
        double dx = RotationStem.X - Center.X;
        double dy = RotationStem.Y - Center.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            dx = 0;
            dy = -1;
            length = 1;
        }
        RotationHandle = new Point(
            RotationStem.X + dx / length * CanvasHandleGeometry.RotationHandleOffset,
            RotationStem.Y + dy / length * CanvasHandleGeometry.RotationHandleOffset);
    }

    public static SelectionFrameGeometry? Create(
        LabelDocument document,
        IReadOnlyCollection<string> selectedElementIds,
        CanvasTransform view,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds = null,
        IReadOnlyDictionary<string, int>? previewRotations = null)
    {
        List<DocumentElement> elements = document.Elements
            .Where(element => selectedElementIds.Contains(element.Id) && document.IsEffectivelyVisible(element))
            .ToList();
        if (elements.Count == 0) return null;

        if (elements.Count == 1)
        {
            DocumentElement element = elements[0];
            MicrometreRect bounds = previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview)
                ? preview
                : element.Bounds;
            int rotation = previewRotations is not null && previewRotations.TryGetValue(element.Id, out int previewRotation)
                ? previewRotation
                : element.RotationMillidegrees;
            return new SelectionFrameGeometry(bounds, rotation, view);
        }

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (DocumentElement element in elements)
        {
            MicrometreRect bounds = previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview)
                ? preview
                : element.Bounds;
            left = Math.Min(left, bounds.X.Value);
            top = Math.Min(top, bounds.Y.Value);
            right = Math.Max(right, bounds.Right.Value);
            bottom = Math.Max(bottom, bounds.Bottom.Value);
        }

        return new SelectionFrameGeometry(
            new MicrometreRect(new(left), new(top), new(right - left), new(bottom - top)),
            0,
            view);
    }

    public static ResizeAnchor ToResizeAnchor(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => ResizeAnchor.TopLeft,
        ResizeHandle.Top => ResizeAnchor.Top,
        ResizeHandle.TopRight => ResizeAnchor.TopRight,
        ResizeHandle.Right => ResizeAnchor.Right,
        ResizeHandle.BottomRight => ResizeAnchor.BottomRight,
        ResizeHandle.Bottom => ResizeAnchor.Bottom,
        ResizeHandle.BottomLeft => ResizeAnchor.BottomLeft,
        ResizeHandle.Left => ResizeAnchor.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(handle)),
    };

    private static Point ToScreen(CanvasTransform view, GeometryPoint point)
    {
        (double x, double y) = view.DocumentToCanvas(
            new Micrometre((int)Math.Round(point.X, MidpointRounding.AwayFromZero)),
            new Micrometre((int)Math.Round(point.Y, MidpointRounding.AwayFromZero)));
        return new Point(x, y);
    }
}
