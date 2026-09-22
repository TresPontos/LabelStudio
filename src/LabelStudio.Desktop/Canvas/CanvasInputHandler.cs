using System.Windows;
using System.Windows.Input;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Snapping;
using LabelStudio.Editor.Transforms;
using LabelStudio.Printing;
using SkiaSharp.Views.Desktop;

namespace LabelStudio.Desktop.Canvas;

public enum InteractionMode
{
    None,
    Selecting,
    Dragging,
    Creating,
    Panning,
    Resizing,
    Rotating,
    AdjustingCutLength,
}

public enum ResizeHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Rotate,
}

public sealed class CanvasInputHandler
{
    private const double HandleSizeDip = 8.0;
    private const double HandleHitToleranceDip = 6.0;

    private readonly EditorState _editor;
    private readonly SkiaCanvasPainter _painter;
    private readonly Action _invalidate;

    private InteractionMode _mode = InteractionMode.None;
    private ResizeHandle _activeResizeHandle = ResizeHandle.None;
    private MicrometrePoint _interactionStart;
    private MicrometreRect _creationStartBounds;
    private double _panStartX;
    private double _panStartY;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private string? _hoverElementId;

    private MicrometreRect _resizeOriginalBounds;
    private AnchoredResizeGesture? _resizeGesture;
    private int _resizeRotationMillidegrees;
    private string? _rotationElementId;
    private int _rotationOriginalMillidegrees;
    private double _rotationStartAngle;
    private List<string> _transformIds = [];
    private Dictionary<string, MicrometreRect> _previewBounds = new(StringComparer.Ordinal);
    private Dictionary<string, int> _previewRotations = new(StringComparer.Ordinal);
    private MicrometreRect _transformOriginalBounds;
    private SnapResult? _currentSnapResult;
    private Micrometre? _previewPageLength;
    private Micrometre _originalPageLength;

    public string? HoverElementId => _hoverElementId;
    public MicrometreRect? CreationPreview =>
        _mode == InteractionMode.Creating ? _creationStartBounds : null;
    public IReadOnlyDictionary<string, MicrometreRect>? PreviewBounds =>
        _mode is InteractionMode.Dragging or InteractionMode.Resizing && _previewBounds.Count > 0
            ? _previewBounds
            : null;
    public IReadOnlyDictionary<string, int>? PreviewRotations =>
        _mode == InteractionMode.Rotating && _previewRotations.Count > 0
            ? _previewRotations
            : null;
    public SnapResult? CurrentSnapResult => _currentSnapResult;
    public Micrometre? PreviewPageLength => _previewPageLength;
    public bool IsAdjustingCutLength => _mode == InteractionMode.AdjustingCutLength;

    public event Action<TextElement>? TextElementCreated;
    public event Action? InteractionChanged;

    public CanvasInputHandler(EditorState editor, SkiaCanvasPainter painter, Action invalidate)
    {
        _editor = editor;
        _painter = painter;
        _invalidate = invalidate;
    }

    public void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point pos = e.GetPosition((IInputElement)sender);
        MicrometrePoint docPoint = _editor.ViewTransform.CanvasToDocument(pos.X, pos.Y);
        Micrometre tolerance = _editor.ViewTransform.ScreenToleranceToDocument(EditorState.ScreenHitToleranceDip);

        if (e.MiddleButton == MouseButtonState.Pressed || _editor.ActiveTool == EditorTool.Pan ||
            (Keyboard.IsKeyDown(Key.Space)))
        {
            _mode = InteractionMode.Panning;
            _panStartX = pos.X;
            _panStartY = pos.Y;
            _panStartOffsetX = _editor.ViewTransform.OffsetX;
            _panStartOffsetY = _editor.ViewTransform.OffsetY;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && HitTestCutHandle(pos))
        {
            StartCutLengthAdjustment();
            ((IInputElement)sender).CaptureMouse();
            return;
        }

        switch (_editor.ActiveTool)
        {
            case EditorTool.Select:
                HandleSelectMouseDown(sender, pos, docPoint, tolerance, e);
                break;
            case EditorTool.Rectangle:
            case EditorTool.Line:
            case EditorTool.Text:
                HandleCreateMouseDown(docPoint);
                break;
        }

        if (_mode is InteractionMode.Dragging or InteractionMode.Resizing or InteractionMode.Rotating or InteractionMode.AdjustingCutLength)
        {
            ((IInputElement)sender).CaptureMouse();
        }
    }

    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition((IInputElement)sender);
        MicrometrePoint docPoint = _editor.ViewTransform.CanvasToDocument(pos.X, pos.Y);
        Micrometre tolerance = _editor.ViewTransform.ScreenToleranceToDocument(EditorState.ScreenHitToleranceDip);

        switch (_mode)
        {
            case InteractionMode.Panning:
                _editor.ViewTransform.OffsetX = _panStartOffsetX + (pos.X - _panStartX);
                _editor.ViewTransform.OffsetY = _panStartOffsetY + (pos.Y - _panStartY);
                _invalidate();
                break;

            case InteractionMode.Dragging:
                int dx = docPoint.X.Value - _interactionStart.X.Value;
                int dy = docPoint.Y.Value - _interactionStart.Y.Value;
                MicrometreRect proposedMove = _transformOriginalBounds.Offset(new(dx), new(dy));
                _currentSnapResult = ApplySnap(proposedMove, SnapOperation.Move, SnapEdges.All);
                dx = _currentSnapResult.Bounds.X.Value - _transformOriginalBounds.X.Value;
                dy = _currentSnapResult.Bounds.Y.Value - _transformOriginalBounds.Y.Value;
                foreach (string id in _transformIds)
                {
                    MicrometreRect orig = _editor.Drag.OriginalBounds[id];
                    _previewBounds[id] = new(
                        new(orig.X.Value + dx), new(orig.Y.Value + dy),
                        orig.Width, orig.Height);
                }
                _invalidate();
                break;

            case InteractionMode.Creating:
                int minX = Math.Min(_interactionStart.X.Value, docPoint.X.Value);
                int minY = Math.Min(_interactionStart.Y.Value, docPoint.Y.Value);
                int w = Math.Abs(docPoint.X.Value - _interactionStart.X.Value);
                int h = Math.Abs(docPoint.Y.Value - _interactionStart.Y.Value);
                _creationStartBounds = new(new(minX), new(minY), new(w), new(h));
                SnapEdges creationEdges = (docPoint.X.Value < _interactionStart.X.Value ? SnapEdges.Left : SnapEdges.Right) |
                    (docPoint.Y.Value < _interactionStart.Y.Value ? SnapEdges.Top : SnapEdges.Bottom);
                _currentSnapResult = ApplySnap(_creationStartBounds, SnapOperation.Resize, creationEdges);
                _creationStartBounds = _currentSnapResult.Bounds;
                _invalidate();
                break;

            case InteractionMode.Resizing:
                HandleResizeMouseMove(docPoint);
                _invalidate();
                break;

            case InteractionMode.Rotating:
                HandleRotationMouseMove(pos);
                _invalidate();
                break;

            case InteractionMode.AdjustingCutLength:
                UpdateCutLength(docPoint);
                _invalidate();
                InteractionChanged?.Invoke();
                break;

            default:
                UpdateHover(docPoint, tolerance);
                break;
        }
    }

    public void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        switch (_mode)
        {
            case InteractionMode.Dragging:
            case InteractionMode.Resizing:
                CommitTransform();
                break;

            case InteractionMode.Rotating:
                CommitRotation();
                break;

            case InteractionMode.Creating:
                CommitCreation();
                break;

            case InteractionMode.AdjustingCutLength:
                CommitCutLength();
                break;
        }

        _mode = InteractionMode.None;
        _resizeGesture = null;
        _previewBounds.Clear();
        _previewRotations.Clear();
        _currentSnapResult = null;
        ((IInputElement)sender).ReleaseMouseCapture();
        InteractionChanged?.Invoke();
        _invalidate();
    }

    public bool IsPointerOverSelectionHandle(Point position) =>
        HitTestHandle(position) != ResizeHandle.None;

    public bool IsPointerOverCutHandle(Point position) => HitTestCutHandle(position);

    public void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point pos = e.GetPosition((IInputElement)sender);
        double zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = _editor.ViewTransform.Zoom * zoomFactor;
        _editor.ViewTransform.ZoomAtPoint(newZoom, pos.X, pos.Y);
        _invalidate();
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        if (_mode != InteractionMode.None && e.Key == Key.Escape)
        {
            if (_mode is InteractionMode.Dragging or InteractionMode.Resizing)
            {
                _editor.Drag.Cancel();
                _previewBounds.Clear();
            }
            else if (_mode == InteractionMode.Rotating)
            {
                _previewRotations.Clear();
                _rotationElementId = null;
            }
            else if (_mode == InteractionMode.AdjustingCutLength)
            {
                _previewPageLength = null;
                _editor.ViewTransform.SetContentSizePreservingDocumentOrigin(
                    _editor.Session.Document.PageDimensions.Width,
                    _originalPageLength);
            }
            _mode = InteractionMode.None;
            _resizeGesture = null;
            _currentSnapResult = null;
            _editor.ActiveTool = EditorTool.Select;
            _invalidate();
            e.Handled = true;
            InteractionChanged?.Invoke();
            return;
        }

        if (_editor.ActiveTool != EditorTool.Select) return;

        int nudge = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)
            ? EditorState.NudgeLargeMicrometres
            : EditorState.NudgeSmallMicrometres;

        MicrometrePoint? direction = e.Key switch
        {
            Key.Left => new MicrometrePoint(new(-nudge), Micrometre.Zero),
            Key.Right => new MicrometrePoint(new(nudge), Micrometre.Zero),
            Key.Up => new MicrometrePoint(Micrometre.Zero, new(-nudge)),
            Key.Down => new MicrometrePoint(Micrometre.Zero, new(nudge)),
            _ => null,
        };

        if (direction is not null && _editor.Selection.HasSelection)
        {
            e.Handled = true;
            LabelDocument doc = _editor.Session.Document;
            IReadOnlyCollection<string> nudgeIds = _editor.Selection.GetTransformableElementIds(doc);
            if (nudgeIds.Count > 0)
            {
                ElementTransform[] transforms = SelectionTransformService.PlanMove(
                    doc, nudgeIds, direction.Value.X.Value, direction.Value.Y.Value);
                _editor.Session.ExecuteCommand(SelectionTransformService.ToCommand(transforms));
            }
            _invalidate();
        }

        if (e.Key == Key.Delete && _editor.Selection.HasSelection)
        {
            e.Handled = true;
            IReadOnlyCollection<string> deleteIds = _editor.Selection.GetSelectedElementIds(_editor.Session.Document);
            _editor.Session.ExecuteCommand(new DeleteElementsCommand(deleteIds.ToList(), _editor.Session.Document));
            _editor.Selection.Clear();
            _invalidate();
        }
    }

    public void Paint(SKPaintSurfaceEventArgs e, float renderScale, string? editingElementId = null)
    {
        _painter.Paint(
            e.Surface.Canvas,
            e.Info.Width,
            e.Info.Height,
            renderScale,
            _editor,
            _hoverElementId,
            CreationPreview,
            PreviewBounds,
            editingElementId,
            PreviewRotations,
            _currentSnapResult?.Indicators,
            _previewPageLength);
    }

    private void HandleSelectMouseDown(object sender, Point pos, MicrometrePoint docPoint, Micrometre tolerance, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) { _invalidate(); return; }

        ResizeHandle handle = HitTestHandle(pos);
        if (handle != ResizeHandle.None && _editor.Selection.HasSelection)
        {
            if (handle == ResizeHandle.Rotate)
            {
                StartRotation(pos);
            }
            else
            {
                StartResize(handle, docPoint);
            }
            return;
        }

        string? hitId = HitTester.HitTestTopmost(_editor.Session.Document, docPoint, tolerance);

        if (hitId is not null)
        {
            string? groupId = _editor.Session.Document.FindGroupIdForMember(hitId);

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (groupId is not null)
                {
                    _editor.Selection.ToggleSelectElement(groupId);
                }
                else
                {
                    _editor.Selection.ToggleSelectElement(hitId);
                }
            }
            else
            {
                if (groupId is not null)
                {
                    if (!_editor.Selection.Contains(groupId))
                    {
                        _editor.Selection.SelectGroup(groupId);
                    }
                }
                else
                {
                    if (!_editor.Selection.Contains(hitId))
                    {
                        _editor.Selection.SelectElement(hitId);
                    }
                }
            }

            LabelDocument doc = _editor.Session.Document;
            IReadOnlyCollection<string> transformableIds = _editor.Selection.GetTransformableElementIds(doc);
            if (transformableIds.Count > 0)
            {
                _mode = InteractionMode.Dragging;
                _interactionStart = docPoint;
                _transformIds = transformableIds.ToList();
                _transformOriginalBounds = SelectionBounds.GetCombinedBounds(doc, transformableIds);
                _previewBounds.Clear();
                _editor.Drag.Begin(transformableIds, doc);
            }
        }
        else
        {
            _editor.Selection.Clear();
        }
        _invalidate();
    }

    private void StartResize(ResizeHandle handle, MicrometrePoint pointerDown)
    {
        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> transformableIds = _editor.Selection.GetTransformableElementIds(doc);
        if (transformableIds.Count == 0) return;

        SelectionFrameGeometry? frame = SelectionFrameGeometry.Create(
            doc,
            transformableIds,
            _editor.ViewTransform);
        if (frame is null) return;
        _mode = InteractionMode.Resizing;
        _activeResizeHandle = handle;
        _transformIds = transformableIds.ToList();
        _resizeOriginalBounds = frame.Bounds;
        _transformOriginalBounds = frame.Bounds;
        _resizeRotationMillidegrees = frame.RotationMillidegrees;
        _resizeGesture = new AnchoredResizeGesture(
            frame.Bounds,
            frame.RotationMillidegrees,
            SelectionFrameGeometry.ToResizeAnchor(handle),
            new GeometryPoint(pointerDown.X.Value, pointerDown.Y.Value));
        _previewBounds.Clear();
        _previewRotations.Clear();
        _editor.Drag.Begin(transformableIds, doc);
    }

    private void StartRotation(Point pos)
    {
        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> ids = _editor.Selection.GetSelectedElementIds(doc);
        if (ids.Count != 1) return;

        string id = ids.Single();
        DocumentElement? element = doc.Elements.FirstOrDefault(item => item.Id == id);
        if (element is null || doc.IsEffectivelyLocked(id)) return;

        SelectionFrameGeometry? frame = SelectionFrameGeometry.Create(
            doc,
            [id],
            _editor.ViewTransform);
        if (frame is null) return;

        _mode = InteractionMode.Rotating;
        _rotationElementId = id;
        _rotationOriginalMillidegrees = element.RotationMillidegrees;
        _rotationStartAngle = Math.Atan2(pos.Y - frame.Center.Y, pos.X - frame.Center.X) * 180.0 / Math.PI;
        _previewRotations.Clear();
        _previewRotations[id] = element.RotationMillidegrees;
    }

    private void HandleRotationMouseMove(Point pos)
    {
        if (_rotationElementId is null) return;

        LabelDocument doc = _editor.Session.Document;
        DocumentElement? element = doc.Elements.FirstOrDefault(item => item.Id == _rotationElementId);
        if (element is null) return;

        SelectionFrameGeometry? frame = SelectionFrameGeometry.Create(
            doc,
            [_rotationElementId],
            _editor.ViewTransform,
            previewRotations: new Dictionary<string, int>
            {
                [_rotationElementId] = _rotationOriginalMillidegrees,
            });
        if (frame is null) return;
        double currentAngle = Math.Atan2(pos.Y - frame.Center.Y, pos.X - frame.Center.X) * 180.0 / Math.PI;
        double delta = currentAngle - _rotationStartAngle;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            delta = Math.Round(delta / 15.0) * 15.0;
        }

        int rotation = ElementGeometry.NormalizeAngle(
            _rotationOriginalMillidegrees + (int)Math.Round(delta * 1000, MidpointRounding.AwayFromZero));
        _previewRotations[_rotationElementId] = rotation;
    }

    private void CommitRotation()
    {
        if (_rotationElementId is null || !_previewRotations.TryGetValue(_rotationElementId, out int rotation)) return;
        if (rotation != _rotationOriginalMillidegrees)
        {
            _editor.Session.ExecuteCommand(new ChangePropertyCommand(
                _rotationElementId,
                "rotationMillidegrees",
                _rotationOriginalMillidegrees,
                rotation));
        }

        _rotationElementId = null;
        _previewRotations.Clear();
    }

    private void CommitTransform()
    {
        IEditorCommand? cmd = _editor.Drag.Commit(
            _editor.Session.Document,
            _previewBounds,
            setTextFrameFixed: _mode == InteractionMode.Resizing);
        if (cmd is not null)
        {
            _editor.Session.ExecuteCommand(cmd);
        }
    }

    private void HandleResizeMouseMove(MicrometrePoint docPoint)
    {
        if (_transformIds.Count == 0 || _resizeGesture is null) return;

        bool proportional = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        MicrometreRect newBounds = _resizeGesture.Update(
            new GeometryPoint(docPoint.X.Value, docPoint.Y.Value),
            SelectionTransformService.MinElementWidthMicrometres,
            SelectionTransformService.MinElementHeightMicrometres,
            proportional);

        newBounds = ApplyResizeSnap(docPoint, newBounds, proportional);

        LabelDocument doc = _editor.Session.Document;
        ElementTransform[] transforms = SelectionTransformService.PlanResize(
            doc, _transformIds, _resizeOriginalBounds, newBounds, proportional: false);
        foreach (ElementTransform transform in transforms)
        {
            _previewBounds[transform.ElementId] = transform.After.Bounds;
        }
    }

    private ResizeHandle HitTestHandle(Point pos)
    {
        if (!_editor.Selection.HasSelection) return ResizeHandle.None;

        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> ids = _editor.Selection.GetSelectedElementIds(doc);
        SelectionFrameGeometry? frame = SelectionFrameGeometry.Create(doc, ids, _editor.ViewTransform);
        if (frame is null) return ResizeHandle.None;

        DocumentElement? soleElement = ids.Count == 1
            ? doc.Elements.FirstOrDefault(element => element.Id == ids.Single())
            : null;
        double rotationHandleDistance = Math.Sqrt(
            Math.Pow(pos.X - frame.RotationHandle.X, 2) +
            Math.Pow(pos.Y - frame.RotationHandle.Y, 2));
        if (soleElement is TextElement && rotationHandleDistance <= CanvasHandleGeometry.RotationHandleHitRadius)
        {
            return ResizeHandle.Rotate;
        }

        double tolerance = HandleHitToleranceDip + HandleSizeDip / 2;
        foreach ((ResizeHandle handle, Point point) in frame.Handles)
        {
            if (Math.Abs(pos.X - point.X) <= tolerance && Math.Abs(pos.Y - point.Y) <= tolerance)
            {
                return handle;
            }
        }

        return ResizeHandle.None;
    }

    private void HandleCreateMouseDown(MicrometrePoint docPoint)
    {
        _transformIds.Clear();
        _mode = InteractionMode.Creating;
        _interactionStart = docPoint;
        _creationStartBounds = new(docPoint.X, docPoint.Y, Micrometre.Zero, Micrometre.Zero);
        _currentSnapResult = null;
    }

    private void CommitCreation()
    {
        if (_creationStartBounds.Width <= Micrometre.Zero || _creationStartBounds.Height <= Micrometre.Zero)
        {
            _creationStartBounds = new(_creationStartBounds.X, _creationStartBounds.Y, new(5000), new(3000));
            _currentSnapResult = ApplySnap(_creationStartBounds, SnapOperation.Move, SnapEdges.All);
            _creationStartBounds = _currentSnapResult.Bounds;
        }

        string id = ElementFactory.GenerateId();
        DocumentElement element = _editor.ActiveTool switch
        {
            EditorTool.Rectangle => ElementFactory.CreateRectangle(id, _creationStartBounds),
            EditorTool.Line => ElementFactory.CreateLine(id,
                new(_creationStartBounds.X, new(_creationStartBounds.Y.Value + _creationStartBounds.Height.Value / 2)),
                new(_creationStartBounds.Right, new(_creationStartBounds.Y.Value + _creationStartBounds.Height.Value / 2)),
                new(200)),
            EditorTool.Text => CreateViewAlignedText(id),
            _ => ElementFactory.CreateRectangle(id, _creationStartBounds),
        };

        _editor.Session.ExecuteCommand(new AddElementCommand(element));
        _editor.Selection.SelectElement(id);
        _editor.ActiveTool = EditorTool.Select;
        if (element is TextElement text)
        {
            TextElementCreated?.Invoke(text);
        }
    }

    private TextElement CreateViewAlignedText(string id)
    {
        TextCreationFrame frame = TextCreationGeometry.FromViewAlignedDrag(
            _creationStartBounds,
            _editor.ViewTransform.ViewRotationDegrees);
        return ElementFactory.CreateText(
            id,
            frame.Bounds,
            string.Empty,
            frame.FontSizePoints) with
        {
            RotationMillidegrees = frame.RotationMillidegrees,
            Overflow = TextOverflowMode.ShrinkToFit,
        };
    }

    private void UpdateHover(MicrometrePoint docPoint, Micrometre tolerance)
    {
        string? newHover = HitTester.HitTestTopmost(_editor.Session.Document, docPoint, tolerance);
        if (newHover != _hoverElementId)
        {
            _hoverElementId = newHover;
            _invalidate();
        }
    }

    private SnapResult ApplySnap(MicrometreRect proposed, SnapOperation operation, SnapEdges edges)
    {
        bool altBypass = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        return SnapEngine.Snap(new SnapRequest(
            _editor.Session.Document,
            proposed,
            operation,
            _editor.ViewTransform.ScreenToleranceToDocument(7))
        {
            Enabled = _editor.SnapEnabled && !altBypass,
            Sources = _editor.SnapSources,
            ResizeEdges = edges,
            ExcludedElementIds = _transformIds,
            PreviousResult = _currentSnapResult,
            StickinessTolerance = _editor.ViewTransform.ScreenToleranceToDocument(2),
        });
    }

    private MicrometreRect ApplyResizeSnap(
        MicrometrePoint pointer,
        MicrometreRect proposedBounds,
        bool proportional)
    {
        if (_resizeGesture is null)
        {
            return proposedBounds;
        }

        GeometryPoint handle = AnchoredResizeGesture.GetHandlePoint(
            proposedBounds,
            _resizeRotationMillidegrees,
            SelectionFrameGeometry.ToResizeAnchor(_activeResizeHandle));
        MicrometrePoint roundedHandle = new(
            new((int)Math.Round(handle.X, MidpointRounding.AwayFromZero)),
            new((int)Math.Round(handle.Y, MidpointRounding.AwayFromZero)));
        MicrometreRect handleBounds = new(
            roundedHandle.X,
            roundedHandle.Y,
            Micrometre.Zero,
            Micrometre.Zero);
        SnapResult handleSnap = ApplySnap(handleBounds, SnapOperation.Move, SnapEdges.All);
        (double deltaX, double deltaY, SnapResult effectiveSnap) =
            ProjectEdgeHandleSnap(handleSnap, roundedHandle);
        MicrometreRect snappedBounds = _resizeGesture.Update(
            new GeometryPoint(pointer.X.Value + deltaX, pointer.Y.Value + deltaY),
            SelectionTransformService.MinElementWidthMicrometres,
            SelectionTransformService.MinElementHeightMicrometres,
            proportional);
        _currentSnapResult = effectiveSnap with { Bounds = snappedBounds };
        return snappedBounds;
    }

    private (double DeltaX, double DeltaY, SnapResult Result) ProjectEdgeHandleSnap(
        SnapResult snap,
        MicrometrePoint handle)
    {
        bool localXAxis = _activeResizeHandle is ResizeHandle.Left or ResizeHandle.Right;
        bool localYAxis = _activeResizeHandle is ResizeHandle.Top or ResizeHandle.Bottom;
        if (!localXAxis && !localYAxis)
        {
            return (
                snap.Bounds.X.Value - handle.X.Value,
                snap.Bounds.Y.Value - handle.Y.Value,
                snap);
        }

        double radians = _resizeRotationMillidegrees / 1000.0 * Math.PI / 180.0;
        double axisX = localXAxis ? Math.Cos(radians) : -Math.Sin(radians);
        double axisY = localXAxis ? Math.Sin(radians) : Math.Cos(radians);
        double tolerance = _editor.ViewTransform.ScreenToleranceToDocument(7).Value;
        List<(double Travel, SnapAxis Axis)> candidates = [];
        if (snap.XSnap is not null && Math.Abs(axisX) > 0.000001)
        {
            double projectedTravel = snap.XSnap.Delta.Value / axisX;
            if (Math.Abs(projectedTravel) <= tolerance) candidates.Add((projectedTravel, SnapAxis.X));
        }
        if (snap.YSnap is not null && Math.Abs(axisY) > 0.000001)
        {
            double projectedTravel = snap.YSnap.Delta.Value / axisY;
            if (Math.Abs(projectedTravel) <= tolerance) candidates.Add((projectedTravel, SnapAxis.Y));
        }

        if (candidates.Count == 0)
        {
            return (0, 0, new SnapResult(
                new(handle.X, handle.Y, Micrometre.Zero, Micrometre.Zero),
                null,
                null,
                Array.Empty<SnapIndicator>()));
        }

        (double selectedTravel, SnapAxis selectedAxis) = candidates.MinBy(candidate => Math.Abs(candidate.Travel));
        AxisSnap? xSnap = selectedAxis == SnapAxis.X ? snap.XSnap : null;
        AxisSnap? ySnap = selectedAxis == SnapAxis.Y ? snap.YSnap : null;
        SnapIndicator[] indicators = snap.Indicators
            .Where(indicator => indicator.Axis == selectedAxis)
            .ToArray();
        SnapResult result = new(snap.Bounds, xSnap, ySnap, indicators);
        return (selectedTravel * axisX, selectedTravel * axisY, result);
    }

    private bool HitTestCutHandle(Point point)
    {
        LabelDocument document = _editor.Session.Document;
        if (document.MediaKind != DocumentMediaKind.Continuous || _editor.ActiveTool != EditorTool.Select)
        {
            return false;
        }

        (Point start, Point end) = GetCutHandleSegment(document.PageDimensions.Height);
        return DistanceToSegment(point, start, end) <= 10;
    }

    private void StartCutLengthAdjustment()
    {
        _mode = InteractionMode.AdjustingCutLength;
        _originalPageLength = _editor.Session.Document.PageDimensions.Height;
        _previewPageLength = _originalPageLength;
        _currentSnapResult = null;
        _editor.Selection.Clear();
        InteractionChanged?.Invoke();
    }

    private void UpdateCutLength(MicrometrePoint pointer)
    {
        LabelDocument document = _editor.Session.Document;
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(document.MediaProfileId);
        int minimum = media.Cutter.MinimumContinuousLength?.Value ?? 1;
        int maximum = media.Cutter.MaximumContinuousLength?.Value ?? int.MaxValue;
        int proposedLength = Math.Clamp(pointer.Y.Value, minimum, maximum);
        MicrometreRect proposed = new(
            Micrometre.Zero,
            Micrometre.Zero,
            document.PageDimensions.Width,
            new Micrometre(proposedLength));
        SnapSources sources = _editor.SnapSources & SnapSources.Grid;
        bool altBypass = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        _currentSnapResult = SnapEngine.Snap(new SnapRequest(
            document,
            proposed,
            SnapOperation.Resize,
            _editor.ViewTransform.ScreenToleranceToDocument(7))
        {
            Enabled = _editor.SnapEnabled && !altBypass,
            Sources = sources,
            ResizeEdges = SnapEdges.Bottom,
            PreviousResult = _currentSnapResult,
            StickinessTolerance = _editor.ViewTransform.ScreenToleranceToDocument(2),
        });
        _previewPageLength = new Micrometre(Math.Clamp(_currentSnapResult.Bounds.Height.Value, minimum, maximum));
        _editor.ViewTransform.SetContentSizePreservingDocumentOrigin(
            document.PageDimensions.Width,
            _previewPageLength.Value);
    }

    private void CommitCutLength()
    {
        if (_previewPageLength is Micrometre length && length != _originalPageLength)
        {
            _editor.Session.ExecuteCommand(new ChangePageLengthCommand(_editor.Session.Document, length));
        }
        _previewPageLength = null;
        LabelDocument document = _editor.Session.Document;
        _editor.ViewTransform.SetContentSize(document.PageDimensions.Width, document.PageDimensions.Height);
    }


    public (Point Start, Point End) GetCutHandleSegment(Micrometre? length = null)
    {
        LabelDocument document = _editor.Session.Document;
        Micrometre y = length ?? _previewPageLength ?? document.PageDimensions.Height;
        CanvasLineSegment segment = ContinuousCutGeometry.GetSegment(
            _editor.ViewTransform,
            document.PageDimensions.Width,
            y);
        return (new Point(segment.Start.X, segment.Start.Y), new Point(segment.End.X, segment.End.Y));
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= double.Epsilon)
        {
            return (point - start).Length;
        }
        double t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        Point nearest = new(start.X + t * dx, start.Y + t * dy);
        return (point - nearest).Length;
    }
}
