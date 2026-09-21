using System.Windows;
using System.Windows.Input;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;
using SkiaSharp.Views.Desktop;
using SelectionTarget = LabelStudio.Editor.Selection.SelectionTarget;

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

    public string? HoverElementId => _hoverElementId;
    public MicrometreRect? CreationPreview =>
        _mode == InteractionMode.Creating ? _creationStartBounds : null;

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
                HandleSelectMouseDown(docPoint, tolerance, e);
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
                IReadOnlyCollection<string> dragIds = _editor.Selection.GetTransformableElementIds(_editor.Session.Document);
                foreach (string id in dragIds)
                {
                    Document.Elements.DocumentElement? elem = _editor.Session.Document.Elements.FirstOrDefault(el => el.Id == id);
                    if (elem is null) continue;
                    MicrometreRect orig = elem.Bounds;
                    MicrometreRect newBounds = new(
                        new(orig.X.Value + dx), new(orig.Y.Value + dy),
                        orig.Width, orig.Height);
                    _editor.Drag.UpdatePreview(id, newBounds);
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
                IEditorCommand? cmd = _editor.Drag.Commit(_editor.Session.Document);
                if (cmd is not null)
                {
                    _editor.Session.ExecuteCommand(cmd);
                }
                break;

            case InteractionMode.Creating:
                CommitCreation();
                break;
        }

        _mode = InteractionMode.None;
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
            if (_mode == InteractionMode.Dragging) _editor.Drag.Cancel();
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
            IReadOnlyCollection<string> nudgeIds = _editor.Selection.GetTransformableElementIds(_editor.Session.Document);
            var changes = new Dictionary<string, (MicrometreRect, MicrometreRect)>();
            foreach (string id in nudgeIds)
            {
                Document.Elements.DocumentElement? elem = _editor.Session.Document.Elements.FirstOrDefault(el => el.Id == id);
                if (elem is null) continue;
                MicrometreRect orig = elem.Bounds;
                MicrometreRect moved = new(
                    new(orig.X.Value + direction.Value.X.Value),
                    new(orig.Y.Value + direction.Value.Y.Value),
                    orig.Width, orig.Height);
                changes[id] = (orig, moved);
            }
            if (changes.Count > 0)
            {
                _editor.Session.ExecuteCommand(new MoveElementsCommand(changes));
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
        _painter.Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height, renderScale, _editor, _hoverElementId, CreationPreview);
    }

    private void HandleSelectMouseDown(MicrometrePoint docPoint, Micrometre tolerance, MouseButtonEventArgs e)
    {
        string? hitId = HitTester.HitTestTopmost(_editor.Session.Document, docPoint, tolerance);

        if (hitId is not null)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                string? groupId = _editor.Session.Document.FindGroupIdForMember(hitId);

                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    if (groupId is not null)
                    {
                        if (_editor.Selection.Contains(groupId))
                        {
                            _editor.Selection.ToggleSelectElement(groupId);
                        }
                        else
                        {
                            _editor.Selection.ToggleSelectElement(groupId);
                        }
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

                IReadOnlyCollection<string> transformableIds = _editor.Selection.GetTransformableElementIds(_editor.Session.Document);
                if (transformableIds.Count > 0 && !_editor.Selection.HasLockedMembers(_editor.Session.Document))
                {
                    _mode = InteractionMode.Dragging;
                    _interactionStart = docPoint;
                    _editor.Drag.Begin(transformableIds.ToList(), _editor.Session.Document);
                }
            }
        }
        else
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _editor.Selection.Clear();
            }
        }
        _invalidate();
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

    private void HandleResizeMouseMove(MicrometrePoint docPoint)
    {
        string? activeId = _editor.Selection.ActiveId;
        if (activeId is null) return;

        Document.Elements.DocumentElement? elem = _editor.Session.Document.Elements.FirstOrDefault(e => e.Id == activeId);
        if (elem is null) return;

        MicrometreRect orig = elem.Bounds;
        int x = orig.X.Value, y = orig.Y.Value, w = orig.Width.Value, h = orig.Height.Value;

        switch (_activeResizeHandle)
        {
            case ResizeHandle.TopLeft:
                x = docPoint.X.Value; y = docPoint.Y.Value;
                w = orig.Right.Value - docPoint.X.Value; h = orig.Bottom.Value - docPoint.Y.Value;
                break;
            case ResizeHandle.Top:
                y = docPoint.Y.Value; h = orig.Bottom.Value - docPoint.Y.Value;
                break;
            case ResizeHandle.TopRight:
                y = docPoint.Y.Value; w = docPoint.X.Value - orig.X.Value; h = orig.Bottom.Value - docPoint.Y.Value;
                break;
            case ResizeHandle.Right:
                w = docPoint.X.Value - orig.X.Value;
                break;
            case ResizeHandle.BottomRight:
                w = docPoint.X.Value - orig.X.Value; h = docPoint.Y.Value - orig.Y.Value;
                break;
            case ResizeHandle.Bottom:
                h = docPoint.Y.Value - orig.Y.Value;
                break;
            case ResizeHandle.BottomLeft:
                x = docPoint.X.Value; w = orig.Right.Value - docPoint.X.Value; h = docPoint.Y.Value - orig.Y.Value;
                break;
            case ResizeHandle.Left:
                x = docPoint.X.Value; w = orig.Right.Value - docPoint.X.Value;
                break;
        }

        w = Math.Max(100, w);
        h = Math.Max(100, h);

        _editor.Drag.UpdatePreview(activeId, new(new(x), new(y), new(w), new(h)));
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