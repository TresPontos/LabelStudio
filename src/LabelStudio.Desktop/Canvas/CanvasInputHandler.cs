using System.Windows;
using System.Windows.Input;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Transforms;
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
    private List<string> _transformIds = [];
    private Dictionary<string, MicrometreRect> _previewBounds = new(StringComparer.Ordinal);

    public string? HoverElementId => _hoverElementId;
    public MicrometreRect? CreationPreview =>
        _mode == InteractionMode.Creating ? _creationStartBounds : null;
    public IReadOnlyDictionary<string, MicrometreRect>? PreviewBounds =>
        _mode is InteractionMode.Dragging or InteractionMode.Resizing && _previewBounds.Count > 0
            ? _previewBounds
            : null;

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
                _invalidate();
                break;

            case InteractionMode.Resizing:
                HandleResizeMouseMove(docPoint);
                _invalidate();
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

            case InteractionMode.Creating:
                CommitCreation();
                break;
        }

        _mode = InteractionMode.None;
        _previewBounds.Clear();
        _invalidate();
    }

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
            _mode = InteractionMode.None;
            _editor.ActiveTool = EditorTool.Select;
            _invalidate();
            e.Handled = true;
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

    public void Paint(SKPaintSurfaceEventArgs e, float renderScale)
    {
        _painter.Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height, renderScale, _editor, _hoverElementId, CreationPreview, PreviewBounds);
    }

    private void HandleSelectMouseDown(object sender, Point pos, MicrometrePoint docPoint, Micrometre tolerance, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) { _invalidate(); return; }

        ResizeHandle handle = HitTestHandle(pos);
        if (handle != ResizeHandle.None && _editor.Selection.HasSelection)
        {
            StartResize(handle);
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

    private void StartResize(ResizeHandle handle)
    {
        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> transformableIds = _editor.Selection.GetTransformableElementIds(doc);
        if (transformableIds.Count == 0) return;

        _mode = InteractionMode.Resizing;
        _activeResizeHandle = handle;
        _transformIds = transformableIds.ToList();
        _resizeOriginalBounds = SelectionBounds.GetCombinedBounds(doc, transformableIds);
        _previewBounds.Clear();
        _editor.Drag.Begin(transformableIds, doc);
    }

    private void CommitTransform()
    {
        IEditorCommand? cmd = _editor.Drag.Commit(_editor.Session.Document, _previewBounds);
        if (cmd is not null)
        {
            _editor.Session.ExecuteCommand(cmd);
        }
    }

    private void HandleResizeMouseMove(MicrometrePoint docPoint)
    {
        if (_transformIds.Count == 0) return;

        MicrometreRect orig = _resizeOriginalBounds;
        int x = orig.X.Value, y = orig.Y.Value;
        int w = orig.Width.Value, h = orig.Height.Value;

        int px = docPoint.X.Value;
        int py = docPoint.Y.Value;

        switch (_activeResizeHandle)
        {
            case ResizeHandle.TopLeft:
                x = Math.Min(px, orig.Right.Value - SelectionTransformService.MinElementWidthMicrometres);
                y = Math.Min(py, orig.Bottom.Value - SelectionTransformService.MinElementHeightMicrometres);
                w = orig.Right.Value - x;
                h = orig.Bottom.Value - y;
                break;
            case ResizeHandle.Top:
                y = Math.Min(py, orig.Bottom.Value - SelectionTransformService.MinElementHeightMicrometres);
                h = orig.Bottom.Value - y;
                break;
            case ResizeHandle.TopRight:
                w = Math.Max(px - orig.X.Value, SelectionTransformService.MinElementWidthMicrometres);
                y = Math.Min(py, orig.Bottom.Value - SelectionTransformService.MinElementHeightMicrometres);
                h = orig.Bottom.Value - y;
                break;
            case ResizeHandle.Right:
                w = Math.Max(px - orig.X.Value, SelectionTransformService.MinElementWidthMicrometres);
                break;
            case ResizeHandle.BottomRight:
                w = Math.Max(px - orig.X.Value, SelectionTransformService.MinElementWidthMicrometres);
                h = Math.Max(py - orig.Y.Value, SelectionTransformService.MinElementHeightMicrometres);
                break;
            case ResizeHandle.Bottom:
                h = Math.Max(py - orig.Y.Value, SelectionTransformService.MinElementHeightMicrometres);
                break;
            case ResizeHandle.BottomLeft:
                x = Math.Min(px, orig.Right.Value - SelectionTransformService.MinElementWidthMicrometres);
                w = orig.Right.Value - x;
                h = Math.Max(py - orig.Y.Value, SelectionTransformService.MinElementHeightMicrometres);
                break;
            case ResizeHandle.Left:
                x = Math.Min(px, orig.Right.Value - SelectionTransformService.MinElementWidthMicrometres);
                w = orig.Right.Value - x;
                break;
        }

        bool proportional = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        MicrometreRect newBounds = new(new(x), new(y), new(w), new(h));

        LabelDocument doc = _editor.Session.Document;
        ElementTransform[] transforms = SelectionTransformService.PlanResize(
            doc, _transformIds, orig, newBounds, proportional);
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
        MicrometreRect bounds = SelectionBounds.GetCombinedBounds(doc, ids);
        if (bounds.Width <= Micrometre.Zero && bounds.Height <= Micrometre.Zero) return ResizeHandle.None;

        CanvasTransform view = _editor.ViewTransform;
        (double left, double top) = view.DocumentToCanvas(bounds.X, bounds.Y);
        (double right, double bottom) = view.DocumentToCanvas(bounds.Right, bounds.Bottom);
        double midX = (left + right) / 2;
        double midY = (top + bottom) / 2;

        double tol = HandleHitToleranceDip + HandleSizeDip / 2;

        bool nearLeft = Math.Abs(pos.X - left) <= tol;
        bool nearRight = Math.Abs(pos.X - right) <= tol;
        bool nearTop = Math.Abs(pos.Y - top) <= tol;
        bool nearBottom = Math.Abs(pos.Y - bottom) <= tol;
        bool nearMidX = Math.Abs(pos.X - midX) <= tol;
        bool nearMidY = Math.Abs(pos.Y - midY) <= tol;

        bool withinY = pos.Y >= top - tol && pos.Y <= bottom + tol;
        bool withinX = pos.X >= left - tol && pos.X <= right + tol;

        if (nearLeft && nearTop && withinX && withinY) return ResizeHandle.TopLeft;
        if (nearRight && nearTop && withinX && withinY) return ResizeHandle.TopRight;
        if (nearLeft && nearBottom && withinX && withinY) return ResizeHandle.BottomLeft;
        if (nearRight && nearBottom && withinX && withinY) return ResizeHandle.BottomRight;
        if (nearMidX && nearTop) return ResizeHandle.Top;
        if (nearMidX && nearBottom) return ResizeHandle.Bottom;
        if (nearMidY && nearLeft) return ResizeHandle.Left;
        if (nearMidY && nearRight) return ResizeHandle.Right;

        return ResizeHandle.None;
    }

    private void HandleCreateMouseDown(MicrometrePoint docPoint)
    {
        _mode = InteractionMode.Creating;
        _interactionStart = docPoint;
        _creationStartBounds = new(docPoint.X, docPoint.Y, Micrometre.Zero, Micrometre.Zero);
    }

    private void CommitCreation()
    {
        if (_creationStartBounds.Width <= Micrometre.Zero || _creationStartBounds.Height <= Micrometre.Zero)
        {
            _creationStartBounds = new(_creationStartBounds.X, _creationStartBounds.Y, new(5000), new(3000));
        }

        string id = ElementFactory.GenerateId();
        DocumentElement element = _editor.ActiveTool switch
        {
            EditorTool.Rectangle => ElementFactory.CreateRectangle(id, _creationStartBounds),
            EditorTool.Line => ElementFactory.CreateLine(id,
                new(_creationStartBounds.X, new(_creationStartBounds.Y.Value + _creationStartBounds.Height.Value / 2)),
                new(_creationStartBounds.Right, new(_creationStartBounds.Y.Value + _creationStartBounds.Height.Value / 2)),
                new(200)),
            EditorTool.Text => ElementFactory.CreateText(id, _creationStartBounds, "Text", 24),
            _ => ElementFactory.CreateRectangle(id, _creationStartBounds),
        };

        _editor.Session.ExecuteCommand(new AddElementCommand(element));
        _editor.Selection.SelectElement(id);
        _editor.ActiveTool = EditorTool.Select;
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
}