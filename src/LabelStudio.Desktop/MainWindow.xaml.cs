using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Clipboard;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Snapping;
using LabelStudio.Editor.Transforms;
using LabelStudio.Desktop.Canvas;
using LabelStudio.Layout;
using LabelStudio.Printing;
using LabelStudio.Printing.BrotherQl;
using LabelStudio.Printing.Mock;
using LabelStudio.Printing.Windows;
using LabelStudio.Rendering;
using LabelStudio.Storage;
using Microsoft.Win32;
using SkiaSharp.Views.Desktop;

namespace LabelStudio.Desktop;

public partial class MainWindow : Window
{
    private EditorState _editor = null!;
    private CanvasInputHandler _input = null!;
    private readonly SkiaCanvasPainter _painter = new();
    private bool _updatingLayers;
    private TextEditSession _textEditSession = null!;
    private bool _updatingTextEditOverlay;
    private string? _newTextEditElementId;
    private readonly SelectionCloneService _cloneService = new();
    private ClipboardPayload? _internalClipboard;
    private Dictionary<string, byte[]>? _documentAssets;
    private readonly RulerLayoutEngine _rulerLayout = new();
    private Point? _rulerPointer;
    private const int DefaultContinuousLengthMicrometres = 100_000;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateNewDocument("brother.dk-22251");
        UpdateCommandButtons();
    }

    private void CreateNewDocument(string mediaProfileId, int viewRotationDegrees = 0)
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(mediaProfileId);
        (PhysicalSize dims, MediaSnapshot snap) = CreateMediaGeometry(media, null);
        DocumentMediaKind mediaKind = media.Kind == MediaKind.Continuous
            ? DocumentMediaKind.Continuous
            : DocumentMediaKind.DieCut;
        LabelDocument doc = LabelDocument.Create(dims, mediaProfileId, snap, mediaKind: mediaKind);

        _editor = new EditorState(doc);
        _input = new CanvasInputHandler(_editor, _painter, InvalidateCanvas);
        _textEditSession = new TextEditSession(_editor.Session, TextLayoutEngine.ResolveAutomaticFrame);
        _input.TextElementCreated += text => BeginTextEdit(text, isNew: true);
        _input.InteractionChanged += () =>
        {
            UpdatePhysicalStatus();
            RedrawRulers();
        };
        _editor.Session.DocumentChanged += (_, _) =>
        {
            InvalidateCanvas();
            UpdateInspector();
            UpdateLayers();
            UpdateTitle();
            UpdateTextEditOverlayPosition();
            _editor.ViewTransform.SetContentSize(
                _editor.Session.Document.PageDimensions.Width,
                _editor.Session.Document.PageDimensions.Height);
            UpdatePhysicalStatus();
            UpdateSnapControls();
            RedrawRulers();
        };
        _editor.Session.DirtyChanged += (_, _) => { UpdateTitle(); UpdateCommandButtons(); };
        _editor.Selection.SelectionChanged += (_, _) =>
        {
            InvalidateCanvas();
            UpdateInspector();
            UpdateLayersSelection();
            UpdatePhysicalStatus();
        };
        _editor.ViewTransform.Reset();
        _editor.ViewTransform.ViewRotationDegrees = viewRotationDegrees;
        CenterView();
        UpdateViewRotationLabel();
        UpdateInspector();
        UpdateLayers();
        UpdateTitle();
        UpdateToolButtons();
        UpdateSnapControls();
        UpdatePhysicalStatus();
        RedrawRulers();
        InvalidateCanvas();
    }

    private void InvalidateCanvas() => CanvasElement.InvalidateVisual();

    private void UpdateTitle()
    {
        string dirty = _editor.Session.IsDirty ? " *" : "";
        string file = _editor.Session.FilePath is not null
            ? System.IO.Path.GetFileName(_editor.Session.FilePath)
            : "Untitled";
        Title = $"LabelStudio - {file}{dirty}";

        DocumentNameLabel.Text = file == "Untitled" ? "Untitled label" : file;
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        if (catalog.TryGet(_editor.Session.Document.MediaProfileId, out MediaProfile? media) && media is not null)
        {
            bool metadataMismatch = !_editor.Session.Document.MediaGeometry.ProfileId.Equals(
                media.ProfileId,
                StringComparison.OrdinalIgnoreCase);
            DocumentMediaLabel.Text = metadataMismatch
                ? $"{media.Sku}  •  media data mismatch"
                : $"{media.Sku}  •  {_editor.Session.Document.PageDimensions.Width.ToMillimetres():0.#} × {_editor.Session.Document.PageDimensions.Height.ToMillimetres():0.#} mm";
        }
        else
        {
            DocumentMediaLabel.Text = $"Unknown roll  •  {_editor.Session.Document.MediaProfileId}";
        }
    }

    private void UpdateCommandButtons()
    {
        UndoBtn.IsEnabled = _editor.Session.History.CanUndo;
        RedoBtn.IsEnabled = _editor.Session.History.CanRedo;
        ZoomLabel.Text = $"{_editor.ViewTransform.Zoom * 100:F0}%";
    }

    // Canvas painting
    private void OnCanvasPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        if (_input is null) return;
        float scale = (float)(CanvasElement.ActualWidth > 0 ? e.Info.Width / CanvasElement.ActualWidth : 1.0);
        _input.Paint(e, scale, _textEditSession.ElementId);
    }

    // Canvas input
    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e) => _input.OnMouseDown(sender, e);
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        _input.OnMouseMove(sender, e);
        _rulerPointer = e.GetPosition(CanvasElement);
        UpdatePointerStatus(_rulerPointer.Value);
        RedrawRulers();
        UpdateTextEditOverlayPosition();
    }
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        _input.OnMouseUp(sender, e);
        UpdateToolButtons();
        UpdatePhysicalStatus();
        RedrawRulers();
    }
    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _input.OnMouseWheel(sender, e);
        UpdateCommandButtons();
        UpdateTextEditOverlayPosition();
        RedrawRulers();
    }

    // Window keyboard
    private void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            if (IsTextEditActive && e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_editor.ActiveTool != EditorTool.Select)
            {
                _editor.ActiveTool = EditorTool.Select;
                UpdateToolButtons();
                InvalidateCanvas();
                e.Handled = true;
                return;
            }
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.Z:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        OnRedo(this, null!);
                    else
                        OnUndo(this, null!);
                    e.Handled = true;
                    break;
                case Key.Y:
                    OnRedo(this, null!);
                    e.Handled = true;
                    break;
                case Key.S:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        OnSaveAs(this, null!);
                    else
                        OnSave(this, null!);
                    e.Handled = true;
                    break;
                case Key.N:
                    OnNew(this, null!);
                    e.Handled = true;
                    break;
                case Key.O:
                    OnOpen(this, null!);
                    e.Handled = true;
                    break;
                case Key.G:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        OnUngroup(this, null!);
                    else
                        OnGroup(this, null!);
                    e.Handled = true;
                    break;
                case Key.C:
                    OnCopy(this, null!);
                    e.Handled = true;
                    break;
                case Key.V:
                    OnPaste(this, null!);
                    e.Handled = true;
                    break;
                case Key.D:
                    OnDuplicate(this, null!);
                    e.Handled = true;
                    break;
            }

            if (e.Handled) return;
        }

        if (e.Key == Key.V) { SetTool(EditorTool.Select); e.Handled = true; }
        if (e.Key == Key.T) { SetTool(EditorTool.Text); e.Handled = true; }
        if (e.Key == Key.R) { SetTool(EditorTool.Rectangle); e.Handled = true; }
        if (e.Key == Key.L) { SetTool(EditorTool.Line); e.Handled = true; }
        if (e.Key == Key.I) { SetTool(EditorTool.Image); e.Handled = true; }
        if (e.Key == Key.H) { SetTool(EditorTool.Pan); e.Handled = true; }

        if (!e.Handled)
            _input.OnKeyDown(e);

        if (e.Handled)
            InvalidateCanvas();
    }

    // Command bar
    private void OnNew(object sender, RoutedEventArgs? e)
    {
        CommitTextEdit();
        if (!PromptSaveIfDirty()) return;
        var dialog = new NewLabelDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            CreateNewDocument(dialog.SelectedMediaProfileId, dialog.SelectedViewRotationDegrees);
        }
    }

    private void OnChangeMedia(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        LabelDocument document = _editor.Session.Document;
        var dialog = new NewLabelDialog(
            document.MediaProfileId,
            changeExisting: true,
            selectedViewRotationDegrees: _editor.ViewTransform.ViewRotationDegrees)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(dialog.SelectedMediaProfileId);
        Micrometre? existingLength = media.Kind == MediaKind.Continuous && document.MediaKind == DocumentMediaKind.Continuous
            ? document.PageDimensions.Height
            : null;
        (PhysicalSize dimensions, MediaSnapshot snapshot) = CreateMediaGeometry(media, existingLength);
        DocumentMediaKind mediaKind = media.Kind == MediaKind.Continuous
            ? DocumentMediaKind.Continuous
            : DocumentMediaKind.DieCut;

        if (document.MediaProfileId.Equals(media.ProfileId, StringComparison.OrdinalIgnoreCase) &&
            document.PageDimensions == dimensions &&
            document.MediaGeometry == snapshot)
        {
            return;
        }

        _editor.Session.ExecuteCommand(new ChangeMediaCommand(document, media.ProfileId, dimensions, snapshot, mediaKind));
        _editor.ViewTransform.ViewRotationDegrees = dialog.SelectedViewRotationDegrees;
        UpdateViewRotationLabel();
        CenterView();
        StatusLabel.Text = $"Roll changed to {media.Sku}. Check artwork placement before printing.";
    }

    private static (PhysicalSize Dimensions, MediaSnapshot Snapshot) CreateMediaGeometry(
        MediaProfile media,
        Micrometre? continuousLength)
    {
        PhysicalSize dimensions = media.Kind == MediaKind.Continuous
            ? new(new(media.PhysicalWidthMicrometres), continuousLength ?? new Micrometre(DefaultContinuousLengthMicrometres))
            : new(new(media.PhysicalWidthMicrometres), new(media.PhysicalLengthMicrometres ?? 0));

        MicrometreRect printable;
        if (media.Kind == MediaKind.Continuous)
        {
            BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(media.ProfileId);
            Micrometre feedMargin = new(PhysicalUnits.DotsToMicrometres(
                mapping.MinimumFeedMarginDots,
                QlContinuousLengthPlanner.Dpi));
            printable = new(
                media.PrintableArea.CrossFeedOffset,
                feedMargin,
                media.PrintableArea.Width,
                new Micrometre(Math.Max(0, dimensions.Height.Value - feedMargin.Value * 2)));
        }
        else
        {
            printable = new(
                media.PrintableArea.CrossFeedOffset,
                media.PrintableArea.FeedOffset,
                media.PrintableArea.Width,
                media.PrintableArea.Length ?? Micrometre.Zero);
        }

        return (dimensions, new MediaSnapshot(media.ProfileId, dimensions, printable));
    }

    private void OnOpen(object sender, RoutedEventArgs? e)
    {
        CommitTextEdit();
        if (!PromptSaveIfDirty()) return;

        OpenFileDialog ofd = new()
        {
            Filter = "Label Studio Document|*.label",
            DefaultExt = ".label",
        };
        if (ofd.ShowDialog() == true)
        {
            try
            {
                LabelDocument doc = DocumentPackage.Load(ofd.FileName);
                _editor.Session.SetDocument(doc, ofd.FileName);
                _editor.Selection.Clear();
                _editor.ViewTransform.Reset();
                MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
                if (catalog.TryGet(doc.MediaProfileId, out MediaProfile? media) && media?.Kind == MediaKind.DieCut)
                {
                    _editor.ViewTransform.ViewRotationDegrees = 90;
                }
                CenterView();
                UpdateViewRotationLabel();
                UpdateInspector();
                UpdateTitle();
                InvalidateCanvas();
                StatusLabel.Text = $"Opened: {ofd.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnSave(object sender, RoutedEventArgs? e)
    {
        if (_editor.Session.FilePath is null)
        {
            OnSaveAs(sender, e);
            return;
        }
        SaveToFile(_editor.Session.FilePath);
    }

    private void OnSaveAs(object sender, RoutedEventArgs? e)
    {
        SaveFileDialog sfd = new()
        {
            Filter = "Label Studio Document|*.label",
            DefaultExt = ".label",
            FileName = "Untitled.label",
        };
        if (sfd.ShowDialog() == true)
        {
            SaveToFile(sfd.FileName);
        }
    }

    private void SaveToFile(string path)
    {
        CommitTextEdit();
        try
        {
            DocumentPackage.Save(_editor.Session.Document, path);
            _editor.Session.MarkSaved(path);
            UpdateTitle();
            StatusLabel.Text = $"Saved: {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs? e)
    {
        CommitTextEdit();
        _editor.Session.Undo();
        _editor.Selection.PruneDeleted(_editor.Session.Document);
        UpdateCommandButtons();
        InvalidateCanvas();
    }

    private void OnRedo(object sender, RoutedEventArgs? e)
    {
        CommitTextEdit();
        _editor.Session.Redo();
        _editor.Selection.PruneDeleted(_editor.Session.Document);
        UpdateCommandButtons();
        InvalidateCanvas();
    }

    // Zoom
    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        _editor.ViewTransform.Zoom = Math.Min(CanvasTransform.MaxZoom, _editor.ViewTransform.Zoom * 1.25);
        UpdateCommandButtons();
        UpdateTextEditOverlayPosition();
        InvalidateCanvas();
        RedrawRulers();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _editor.ViewTransform.Zoom = Math.Max(CanvasTransform.MinZoom, _editor.ViewTransform.Zoom / 1.25);
        UpdateCommandButtons();
        UpdateTextEditOverlayPosition();
        InvalidateCanvas();
        RedrawRulers();
    }

    private void OnZoomFit(object sender, RoutedEventArgs e)
    {
        CenterView();
        InvalidateCanvas();
    }

    // View Rotation
    private void OnRotateViewLeft(object sender, RoutedEventArgs e)
    {
        _editor.ViewTransform.ViewRotationDegrees -= 90;
        UpdateViewRotationLabel();
        CenterView();
        UpdateTextEditOverlayPosition();
        InvalidateCanvas();
    }

    private void OnRotateViewRight(object sender, RoutedEventArgs e)
    {
        _editor.ViewTransform.ViewRotationDegrees += 90;
        UpdateViewRotationLabel();
        CenterView();
        UpdateTextEditOverlayPosition();
        InvalidateCanvas();
    }

    private void CenterView()
    {
        PhysicalSize dims = _editor.Session.Document.PageDimensions;
        _editor.ViewTransform.FitToViewportTopLeft(
            dims.Width,
            dims.Height,
            CanvasHost.ActualWidth,
            CanvasHost.ActualHeight,
            16);

        UpdateCommandButtons();
        UpdateTextEditOverlayPosition();
        RedrawRulers();
    }

    private void UpdateViewRotationLabel()
    {
        ViewRotationLabel.Text = $"{_editor.ViewTransform.ViewRotationDegrees}\u00B0";
        UpdateCommandButtons();
        RedrawRulers();
    }

    private static int GetRasterHeight(
        LabelDocument document,
        MediaProfile media,
        BrotherQlMediaMapping mapping) =>
        media.Kind == MediaKind.DieCut
            ? mapping.PrintableLengthDots
            : QlContinuousLengthPlanner.Plan(document.PageDimensions.Height, mapping).RasterRows;

    private void OnSnapToggle(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.SnapEnabled = !_editor.SnapEnabled;
        UpdateSnapControls();
        InvalidateCanvas();
    }

    private void OnShowGridToggle(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.ShowGrid = ShowGridToggle.IsChecked == true;
        InvalidateCanvas();
    }

    private void OnShowPrintLimitsToggle(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.ShowPrintLimits = ShowPrintLimitsToggle.IsChecked == true;
        InvalidateCanvas();
    }

    private void OnShowSafeAreaToggle(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.ShowSafeArea = ShowSafeAreaToggle.IsChecked == true;
        if (!_editor.ShowSafeArea && StatusLabel.Text == "Outside safe area")
        {
            StatusLabel.Text = "Ready";
        }
        UpdatePhysicalStatus();
        InvalidateCanvas();
    }

    private void OnGridSpacingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_editor is null || GridSpacingCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string raw || !int.TryParse(raw, out int spacing)) return;

        LabelDocument document = _editor.Session.Document;
        if (document.DesignMetadata.Grid.XSpacing.Value == spacing &&
            document.DesignMetadata.Grid.YSpacing.Value == spacing) return;

        DocumentGridGeometry grid = document.DesignMetadata.Grid with
        {
            XSpacing = new Micrometre(spacing),
            YSpacing = new Micrometre(spacing),
        };
        DocumentDesignMetadata metadata = new(
            document.DesignMetadata.Groups,
            document.DesignMetadata.Guides,
            grid,
            document.DesignMetadata.SafeMargins);
        _editor.Session.ExecuteCommand(new ChangeDesignMetadataCommand(document.DesignMetadata, metadata));
    }

    private void UpdateSnapControls()
    {
        if (_editor is null) return;
        SnapToggle.Content = _editor.SnapEnabled ? "Snap: ON" : "Snap: OFF";
        SnapStatusLabel.Text = _editor.SnapEnabled ? "Snap: ON" : "Snap: OFF";
        ShowGridToggle.IsChecked = _editor.ShowGrid;
        ShowPrintLimitsToggle.IsChecked = _editor.ShowPrintLimits;
        ShowSafeAreaToggle.IsChecked = _editor.ShowSafeArea;
        ShowSafeAreaToggle.Content =
            $"Safe {_editor.Session.Document.DesignMetadata.SafeMargins.Left.ToMillimetres():0.#} mm";
    }

    private void UpdatePhysicalStatus()
    {
        if (_editor is null) return;
        LabelDocument document = _editor.Session.Document;
        Micrometre length = _input?.PreviewPageLength ?? document.PageDimensions.Height;
        string cut = document.MediaKind == DocumentMediaKind.Continuous
            ? $" · Cut {length.ToMillimetres():0.0} mm"
            : string.Empty;
        LabelSizeStatusLabel.Text =
            $"Label: {document.PageDimensions.Width.ToMillimetres():0.#} × {length.ToMillimetres():0.#} mm{cut}";

        if (_editor.ShowSafeArea && _editor.Selection.HasSelection)
        {
            IReadOnlyCollection<string> ids = _editor.Selection.GetSelectedElementIds(document);
            MicrometreRect bounds = SelectionBounds.GetCombinedBounds(document, ids);
            MicrometreRect safe = DocumentPrintableGeometry.GetSafeArea(document);
            if (bounds.X < safe.X || bounds.Y < safe.Y || bounds.Right > safe.Right || bounds.Bottom > safe.Bottom)
            {
                StatusLabel.Text = "Outside safe area";
            }
        }
    }

    private void UpdatePointerStatus(Point point)
    {
        MicrometrePoint documentPoint = _editor.ViewTransform.CanvasToDocument(point.X, point.Y);
        PointerStatusLabel.Text = $"X: {documentPoint.X.ToMillimetres():0.0} mm  Y: {documentPoint.Y.ToMillimetres():0.0} mm";
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        _rulerPointer = null;
        PointerStatusLabel.Text = "X: --  Y: --";
        RedrawRulers();
    }

    private void OnCanvasHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_editor is null) return;
        RedrawRulers();
        UpdateTextEditOverlayPosition();
    }

    private void RedrawRulers()
    {
        if (_editor is null || HorizontalRuler.ActualWidth <= 0 || VerticalRuler.ActualHeight <= 0) return;
        HorizontalRuler.Children.Clear();
        VerticalRuler.Children.Clear();
        DrawRuler(HorizontalRuler, _rulerLayout.CreateLayout(
            _editor.ViewTransform, RulerAxis.Horizontal, 0, HorizontalRuler.ActualWidth));
        DrawRuler(VerticalRuler, _rulerLayout.CreateLayout(
            _editor.ViewTransform, RulerAxis.Vertical, 0, VerticalRuler.ActualHeight));

        if (_rulerPointer is Point pointer)
        {
            DrawRulerCursor(HorizontalRuler, RulerAxis.Horizontal, pointer.X);
            DrawRulerCursor(VerticalRuler, RulerAxis.Vertical, pointer.Y);
        }
    }

    private void DrawRuler(System.Windows.Controls.Canvas canvas, RulerLayout layout)
    {
        bool horizontal = layout.Axis == RulerAxis.Horizontal;
        foreach (RulerTick tick in layout.Ticks)
        {
            var line = new System.Windows.Shapes.Line
            {
                Stroke = (Brush)FindResource(tick.IsMajor ? "TextSecondaryBrush" : "TextMutedBrush"),
                StrokeThickness = 1,
            };
            if (horizontal)
            {
                line.X1 = line.X2 = tick.Position;
                line.Y1 = tick.IsMajor ? 8 : 14;
                line.Y2 = 24;
            }
            else
            {
                line.X1 = tick.IsMajor ? 12 : 18;
                line.X2 = 30;
                line.Y1 = line.Y2 = tick.Position;
            }
            canvas.Children.Add(line);

            if (tick.IsMajor && tick.Label is not null)
            {
                var label = new TextBlock
                {
                    Text = tick.ValueMillimetres == 0 ? "0" : tick.Label,
                    FontSize = 9,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                };
                if (horizontal)
                {
                    System.Windows.Controls.Canvas.SetLeft(label, tick.Position + 2);
                    System.Windows.Controls.Canvas.SetTop(label, 0);
                }
                else
                {
                    System.Windows.Controls.Canvas.SetLeft(label, 1);
                    System.Windows.Controls.Canvas.SetTop(label, tick.Position + 1);
                }
                canvas.Children.Add(label);
            }
        }
    }

    private static void DrawRulerCursor(System.Windows.Controls.Canvas canvas, RulerAxis axis, double position)
    {
        var marker = new System.Windows.Shapes.Line
        {
            Stroke = Brushes.DeepPink,
            StrokeThickness = 1.5,
        };
        if (axis == RulerAxis.Horizontal)
        {
            marker.X1 = marker.X2 = position;
            marker.Y1 = 0;
            marker.Y2 = 24;
        }
        else
        {
            marker.X1 = 0;
            marker.X2 = 30;
            marker.Y1 = marker.Y2 = position;
        }
        canvas.Children.Add(marker);
    }

    // Tools
    private void OnToolSelect(object sender, RoutedEventArgs e) => SetTool(EditorTool.Select);
    private void OnToolText(object sender, RoutedEventArgs e) => SetTool(EditorTool.Text);
    private void OnToolRect(object sender, RoutedEventArgs e) => SetTool(EditorTool.Rectangle);
    private void OnToolLine(object sender, RoutedEventArgs e) => SetTool(EditorTool.Line);
    private void OnToolImage(object sender, RoutedEventArgs e) => SetTool(EditorTool.Image);
    private void OnToolPan(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pan);

    private void SetTool(EditorTool tool)
    {
        CommitTextEdit();
        _editor.ActiveTool = tool;
        UpdateToolButtons();
        InvalidateCanvas();
    }

    private void UpdateToolButtons()
    {
        foreach (UIElement? child in ((StackPanel)ToolSelect.Parent).Children)
        {
            if (child is Button btn)
            {
                btn.Background = System.Windows.Media.Brushes.Transparent;
                btn.Foreground = (Brush)FindResource("InkSoftBrush");
            }
        }
        Button? activeBtn = _editor.ActiveTool switch
        {
            EditorTool.Select => ToolSelect,
            EditorTool.Text => ToolText,
            EditorTool.Rectangle => ToolRect,
            EditorTool.Line => ToolLine,
            EditorTool.Image => ToolImage,
            EditorTool.Pan => ToolPan,
            _ => null,
        };
        if (activeBtn is not null)
        {
            activeBtn.Background = (Brush)FindResource("AccentBrush");
            activeBtn.Foreground = System.Windows.Media.Brushes.White;
        }
    }

    // Thermal preview
    private void OnThermalPreview(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        try
        {
            LabelDocument doc = _editor.Session.Document;
            PreparedScene scene = new LayoutEngine().Prepare(doc);
            MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
            if (!catalog.TryGet(doc.MediaProfileId, out MediaProfile? media) || media is null) return;

            BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(media.ProfileId);
            int rasterHeight = GetRasterHeight(doc, media, mapping);
            MicrometreRect printable = DocumentPrintableGeometry.GetMediaPrintableArea(doc);

            RenderTarget target = new(
                300, 300, 720, rasterHeight,
                printable,
                mapping.HeadLeftBlankDots, mapping.PrintableWidthDots,
                [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)])
            {
                DocumentOriginX = printable.X,
                DocumentOriginY = printable.Y,
            };

            RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
            var preview = new ThermalPreviewWindow(planes) { Owner = this };
            preview.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Thermal preview failed.\n\n{ex}",
                "Thermal Preview Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Mock print
    private void OnMockPrint(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        LabelDocument doc = _editor.Session.Document;
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        if (!catalog.TryGet(doc.MediaProfileId, out MediaProfile? media) || media is null) return;

        PrintSettings settings = new(doc.PrintDefaults.DefaultInk, doc.PrintDefaults.AutoCut, doc.PrintDefaults.CutAtEnd, doc.PrintDefaults.Dpi);

        PreflightResult preflight = new PreflightEngine().Check(doc, scene, media, settings);
        if (!preflight.CanPrint)
        {
            string errors = string.Join("\n", preflight.Errors.Select(x => $"{x.Code}: {x.Message}"));
            MessageBox.Show($"Preflight failed:\n\n{errors}", "Cannot Print", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(media.ProfileId);
        int rasterHeight = GetRasterHeight(doc, media, mapping);
        MicrometreRect printable = DocumentPrintableGeometry.GetMediaPrintableArea(doc);

        RenderTarget target = new(
            settings.Dpi, settings.Dpi, 720, rasterHeight,
            printable,
            mapping.HeadLeftBlankDots, mapping.PrintableWidthDots,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)])
        {
            DocumentOriginX = printable.X,
            DocumentOriginY = printable.Y,
        };

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        PrintIntent intent = new(doc.Id, scene, media, settings);
        DevicePrintJob job = new(intent, planes, media, settings, $"Mock-{doc.Id}");

        string outputDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LabelStudio", "MockOutput");

        MockFileBackend mock = new(outputDir);
        PrintResult result = mock.Print(job);

        if (result.Success)
            StatusLabel.Text = $"Mock print saved to: {outputDir}";
        else
            MessageBox.Show(result.Error ?? "Unknown error", "Mock Print Failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // Physical print
    private void OnPrint(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        var dialog = new PrintDialogWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            string queueName = dialog.PrinterQueueName;
            if (string.IsNullOrEmpty(queueName))
            {
                MessageBox.Show("No printer queue specified.", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                LabelDocument doc = _editor.Session.Document;
                PreparedScene scene = new LayoutEngine().Prepare(doc);
                MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
                if (!catalog.TryGet(doc.MediaProfileId, out MediaProfile? media) || media is null) return;

                PrintSettings settings = new(
                    doc.PrintDefaults.DefaultInk,
                    doc.PrintDefaults.AutoCut,
                    doc.PrintDefaults.CutAtEnd,
                    doc.PrintDefaults.Dpi);

                PreflightResult preflight = new PreflightEngine().Check(doc, scene, media, settings);
                if (!preflight.CanPrint)
                {
                    string errors = string.Join("\n", preflight.Errors.Select(x => $"{x.Code}: {x.Message}"));
                    MessageBox.Show($"Preflight failed:\n\n{errors}", "Cannot Print", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(media.ProfileId);
                int rasterHeight = GetRasterHeight(doc, media, mapping);
                MicrometreRect printable = DocumentPrintableGeometry.GetMediaPrintableArea(doc);
                RenderTarget target = new(
                    settings.Dpi, settings.Dpi, 720, rasterHeight,
                    printable,
                    mapping.HeadLeftBlankDots, mapping.PrintableWidthDots,
                    [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)])
                {
                    DocumentOriginX = printable.X,
                    DocumentOriginY = printable.Y,
                };

                RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
                PrintIntent intent = new(doc.Id, scene, media, settings);
                DevicePrintJob job = new(intent, planes, media, settings, $"Physical-{doc.Id}");

                Ql800PrinterBackend backend = new(
                    transport: new WindowsRawTransport(),
                    transportTarget: new PrinterTransportTarget(queueName));
                PrintResult result = backend.Print(job);

                if (result.Success)
                    StatusLabel.Text = $"Print job sent to {queueName}";
                else
                    MessageBox.Show(
                        result.Error ?? "Print failed without an error detail from the printer backend.",
                        "Print Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to send print job to '{queueName}'.\n\nEnsure the printer is connected, powered on, and the queue name matches exactly.\n\n{ex.GetType().Name}: {ex.Message}",
                    "Print Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    // Closing
    private void MainWindow_OnClosing(object sender, CancelEventArgs e)
    {
        CommitTextEdit();
        if (!PromptSaveIfDirty())
        {
            e.Cancel = true;
        }
    }

    private bool PromptSaveIfDirty()
    {
        if (!_editor.Session.IsDirty) return true;
        var dialog = new UnsavedChangesDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.SaveChanges)
        {
            OnSave(this, null!);
            return !_editor.Session.IsDirty;
        }

        return true;
    }

    private void OnCanvasPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        Point pos = e.GetPosition(CanvasElement);
        if (e.OriginalSource is DependencyObject source &&
            (ReferenceEquals(source, TextEditOverlay) || TextEditOverlay.IsAncestorOf(source)))
        {
            return;
        }

        MicrometrePoint docPoint = _editor.ViewTransform.CanvasToDocument(pos.X, pos.Y);
        Micrometre tolerance = _editor.ViewTransform.ScreenToleranceToDocument(EditorState.ScreenHitToleranceDip);
        string? hitId = HitTester.HitTestTopmost(_editor.Session.Document, docPoint, tolerance);
        TextElement? textElement = hitId is null
            ? null
            : _editor.Session.Document.Elements.OfType<TextElement>().FirstOrDefault(text => text.Id == hitId);

        bool canEditHitText = textElement is not null &&
            _editor.ActiveTool is EditorTool.Select or EditorTool.Text &&
            !_input.IsPointerOverSelectionHandle(pos) &&
            !_input.IsPointerOverCutHandle(pos) &&
            IsTextContentPoint(textElement, docPoint);

        if (canEditHitText)
        {
            if (IsTextEditActive && _textEditSession.ElementId != textElement!.Id)
            {
                CommitTextEdit();
            }

            _editor.Selection.SelectElement(textElement!.Id);
            BeginTextEdit(textElement, pos);
            e.Handled = true;
            return;
        }

        if (IsTextEditActive)
        {
            CommitTextEdit();
        }
    }

    private bool IsTextEditActive => _textEditSession.IsActive;

    private void BeginTextEdit(TextElement text, Point? canvasPoint = null, bool isNew = false)
    {
        if (_editor.Session.Document.IsEffectivelyLocked(text.Id) ||
            !_editor.Session.Document.IsEffectivelyVisible(text.Id)) return;

        if (!IsTextEditActive)
        {
            _textEditSession.Begin(text.Id);
            _newTextEditElementId = isNew ? text.Id : null;
        }

        _updatingTextEditOverlay = true;
        TextEditOverlay.Text = text.Text;
        _updatingTextEditOverlay = false;
        TextEditOverlay.FontFamily = new FontFamily(text.FontFamily ?? "Segoe UI");
        TextEditOverlay.Foreground = text.Ink == InkChannel.Red ? Brushes.DarkRed : Brushes.Black;
        ApplyTextOverlayLayout(text);
        TextEditOverlay.Visibility = Visibility.Visible;
        TextEditOverlay.IsHitTestVisible = true;
        OverlayCanvas.IsHitTestVisible = true;

        UpdateTextEditOverlayPosition(text);

        TextEditOverlay.Focus();
        if (canvasPoint is null)
        {
            TextEditOverlay.CaretIndex = TextEditOverlay.Text.Length;
            TextEditOverlay.SelectionLength = 0;
        }
        else
        {
            Point click = canvasPoint.Value;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                Point local = OverlayCanvas.TranslatePoint(click, TextEditOverlay);
                int index = TextEditOverlay.GetCharacterIndexFromPoint(local, true);
                TextEditOverlay.CaretIndex = index < 0 ? TextEditOverlay.Text.Length : index;
                TextEditOverlay.SelectionLength = 0;
            });
        }
        InvalidateCanvas();
    }

    private void CommitTextEdit()
    {
        if (!IsTextEditActive) return;
        _textEditSession.Commit();
        _newTextEditElementId = null;
        ExitTextEdit();
    }

    private void CancelTextEdit()
    {
        string? newElementId = _newTextEditElementId;
        _textEditSession.Cancel();
        _newTextEditElementId = null;
        if (newElementId is not null &&
            _editor.Session.Document.Elements.Any(element => element.Id == newElementId) &&
            _editor.Session.History.CanUndo)
        {
            _editor.Session.Undo();
            _editor.Selection.Clear();
        }
        ExitTextEdit();
        InvalidateCanvas();
    }

    private void ExitTextEdit()
    {
        TextEditOverlay.Visibility = Visibility.Collapsed;
        TextEditOverlay.IsHitTestVisible = false;
        OverlayCanvas.IsHitTestVisible = false;
        TextEditOverlay.RenderTransform = null;
    }

    private void OnTextEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (IsTextEditActive)
        {
            CommitTextEdit();
        }
    }

    private void OnTextEditKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsTextEditActive) return;

        if (e.Key == Key.Escape)
        {
            CancelTextEdit();
            e.Handled = true;
        }
    }

    private void OnTextEditTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingTextEditOverlay && IsTextEditActive)
        {
            _textEditSession.Update(TextEditOverlay.Text);
            UpdateTextEditOverlayPosition();
            InvalidateCanvas();
        }
    }

    private void UpdateTextEditOverlayPosition(TextElement? editedElement = null)
    {
        if (!IsTextEditActive) return;

        TextElement? text = editedElement ?? _editor.Session.Document.Elements
            .OfType<TextElement>()
            .FirstOrDefault(element => element.Id == _textEditSession.ElementId);
        if (text is null)
        {
            ExitTextEdit();
            return;
        }

        CanvasTransform view = _editor.ViewTransform;
        MicrometreRect bounds = text.Bounds;
        MicrometrePoint centre = new(
            new(bounds.X.Value + bounds.Width.Value / 2),
            new(bounds.Y.Value + bounds.Height.Value / 2));
        (double centreX, double centreY) = view.DocumentToCanvas(centre);
        double frameWidth = view.DocumentToCanvasLength(bounds.Width);
        double frameHeight = view.DocumentToCanvasLength(bounds.Height);
        TextEditOverlay.Width = Math.Max(1, frameWidth);
        TextEditOverlay.Height = Math.Max(1, frameHeight);
        ApplyTextOverlayLayout(text);
        TextLayoutResult layout = TextLayoutEngine.Layout(
            text.Text,
            text.FontFamily,
            (float)Math.Max(0.1, text.FontSizePoints * (96.0 / 72.0) * view.Zoom),
            (float)((96.0 / 72.0) * view.Zoom),
            (float)frameWidth,
            (float)frameHeight,
            text.Wrapping,
            text.Overflow,
            text.HorizontalAlignment,
            text.VerticalAlignment);
        TextEditOverlay.FontSize = layout.EffectiveFontSizePixels;
        TextEditOverlay.RenderTransformOrigin = new Point(0.5, 0.5);
        TextEditOverlay.RenderTransform = new RotateTransform(
            view.ViewRotationDegrees + text.RotationMillidegrees / 1000.0);

        System.Windows.Controls.Canvas.SetLeft(TextEditOverlay, centreX - TextEditOverlay.Width / 2);
        System.Windows.Controls.Canvas.SetTop(TextEditOverlay, centreY - TextEditOverlay.Height / 2);
    }

    private void ApplyTextOverlayLayout(TextElement text)
    {
        TextEditOverlay.TextWrapping = text.Wrapping == TextWrappingMode.Wrap
            ? System.Windows.TextWrapping.Wrap
            : System.Windows.TextWrapping.NoWrap;
        TextEditOverlay.HorizontalContentAlignment = text.HorizontalAlignment switch
        {
            TextHorizontalAlignment.Center => System.Windows.HorizontalAlignment.Center,
            TextHorizontalAlignment.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Left,
        };
        TextEditOverlay.VerticalContentAlignment = text.VerticalAlignment switch
        {
            TextVerticalAlignment.Middle => System.Windows.VerticalAlignment.Center,
            TextVerticalAlignment.Bottom => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Top,
        };
    }

    private bool IsTextContentPoint(TextElement text, MicrometrePoint documentPoint)
    {
        GeometryPoint local = ElementGeometry.InverseRotatePoint(documentPoint, text.Bounds, text.RotationMillidegrees);
        double inset = _editor.ViewTransform.ScreenToleranceToDocument(5).Value;
        inset = Math.Min(inset, Math.Min(text.Bounds.Width.Value, text.Bounds.Height.Value) / 4.0);
        return local.X >= text.Bounds.X.Value + inset &&
            local.X <= text.Bounds.Right.Value - inset &&
            local.Y >= text.Bounds.Y.Value + inset &&
            local.Y <= text.Bounds.Bottom.Value - inset;
    }
    private bool _suppressContextUpdates;

    private TextElement? GetSingleSelectedTextElement()
    {
        if (!_editor.Selection.HasSelection) return null;
        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> ids = _editor.Selection.GetSelectedElementIds(doc);
        if (ids.Count != 1) return null;
        string? id = ids.Single();
        return doc.Elements.OfType<TextElement>().FirstOrDefault(t => t.Id == id);
    }

    private void UpdateContextToolbar()
    {
        if (_suppressContextUpdates) return;
        TextElement? text = GetSingleSelectedTextElement();

        if (text is null)
        {
            TextContextPanel.Opacity = 0.45;
            ContextFontFamilyCombo.IsEnabled = false;
            ContextFontSizeBox.IsEnabled = false;
            ContextTextStatus.Text = "Select text to format";
            ContextFontFamilyCombo.Text = "";
            ContextFontSizeBox.Text = "";
            return;
        }

        TextContextPanel.Opacity = 1.0;
        ContextFontFamilyCombo.IsEnabled = true;
        ContextFontSizeBox.IsEnabled = true;
        ContextTextStatus.Text = text.Text.Length > 20 ? text.Text[..20] + "…" : (text.Text.Length == 0 ? "Empty text" : text.Text);

        _suppressContextUpdates = true;
        ContextFontFamilyCombo.Text = text.FontFamily ?? "Segoe UI";
        ContextFontSizeBox.Text = text.FontSizePoints.ToString("0");
        _suppressContextUpdates = false;
    }

    private void OnContextFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressContextUpdates) return;
        TextElement? text = GetSingleSelectedTextElement();
        if (text is null) return;
        string family = (ContextFontFamilyCombo.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(family)) return;
        if (text.FontFamily == family) return;
        CommitTextProperty(text.Id, "fontFamily", text.FontFamily ?? "", (object)family);
    }

    private void OnContextFontFamilyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressContextUpdates) return;
        TextElement? text = GetSingleSelectedTextElement();
        if (text is null) return;
        string family = (ContextFontFamilyCombo.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(family)) return;
        if (text.FontFamily == family) return;
        CommitTextProperty(text.Id, "fontFamily", text.FontFamily ?? "", (object)family);
    }

    private void CommitContextFontSize(int newSize)
    {
        TextElement? text = GetSingleSelectedTextElement();
        if (text is null) return;
        newSize = Math.Clamp(newSize, 4, 200);
        if (text.FontSizePoints == newSize) return;
        CommitTextProperty(text.Id, "fontSizePoints", text.FontSizePoints, newSize);
    }

    private void OnContextFontSizeLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressContextUpdates) return;
        if (int.TryParse(ContextFontSizeBox.Text, out int size))
            CommitContextFontSize(size);
        else if (GetSingleSelectedTextElement() is { } text)
            ContextFontSizeBox.Text = text.FontSizePoints.ToString("0");
    }

    private void OnContextFontSizeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(ContextFontSizeBox.Text, out int size))
            CommitContextFontSize(size);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void OnContextFontSizeUp(object sender, RoutedEventArgs e)
    {
        TextElement? text = GetSingleSelectedTextElement();
        if (text is null) return;
        CommitContextFontSize(text.FontSizePoints + 1);
    }

    private void OnContextFontSizeDown(object sender, RoutedEventArgs e)
    {
        TextElement? text = GetSingleSelectedTextElement();
        if (text is null) return;
        CommitContextFontSize(text.FontSizePoints - 1);
    }

    private void UpdateInspector()
    {
        InspectorPanel.Children.Clear();
        UpdateContextToolbar();

        if (!_editor.Selection.HasSelection)
        {
            AddDocumentInspector();
            return;
        }

        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> elementIds = _editor.Selection.GetSelectedElementIds(doc);
        IReadOnlyList<DocumentElement> selected = doc.Elements
            .Where(element => elementIds.Contains(element.Id))
            .ToList();

        if (_editor.Selection.ActiveGroupId is not null)
        {
            ElementGroup? group = doc.DesignMetadata.Groups
                .FirstOrDefault(g => string.Equals(g.Id, _editor.Selection.ActiveGroupId, StringComparison.Ordinal));
            if (group is not null)
            {
                AddInspectorRow("Type", "Group", false);
                AddInspectorTextField("Name", group.Name ?? string.Empty, value =>
                    CommitGroupName(group.Id, value));
                MicrometreRect groupBounds = GroupGeometry.GetGroupBounds(doc, group.Id);
                AddInspectorRow("X", $"{groupBounds.X.ToMillimetres():F1} mm", false);
                AddInspectorRow("Y", $"{groupBounds.Y.ToMillimetres():F1} mm", false);
                AddInspectorRow("Width", $"{groupBounds.Width.ToMillimetres():F1} mm", false);
                AddInspectorRow("Height", $"{groupBounds.Height.ToMillimetres():F1} mm", false);
                AddInspectorRow("Members", $"{group.MemberIds.Count}", false);

                bool groupLocked = group.IsLocked || selected.Any(e => e.IsLocked);
                if (groupLocked)
                {
                    AddInspectorSeparator();
                    AddInspectorRow("Locked", "Yes - transform disabled", false);
                }
                return;
            }
        }

        if (_editor.Selection.IsMultiSelect)
        {
            AddInspectorRow("Selection", $"{selected.Count} elements", false);
            AddInspectorMetadataChecks(selected);
            AddInspectorSeparator();

            List<string> transformIds = SelectionBounds.ResolveTransformableElementIds(doc, _editor.Selection)
                .Where(id => doc.IsEffectivelyVisible(id))
                .ToList();
            bool locked = SelectionBounds.ContainsLockedMember(doc, transformIds);
            if (locked)
            {
                AddInspectorRow("Locked", "Yes - transform disabled", false);
                return;
            }

            MicrometreRect combined = SelectionBounds.GetCombinedBounds(doc, transformIds);
            AddInspectorField("X", combined.X.ToMillimetres(), "X", mm => CommitSelectionMoveTo(
                transformIds, (int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero), combined.Y.Value));
            AddInspectorField("Y", combined.Y.ToMillimetres(), "Y", mm => CommitSelectionMoveTo(
                transformIds, combined.X.Value, (int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)));
            AddInspectorField("Width", combined.Width.ToMillimetres(), "Width", mm => CommitSelectionScaleTo(
                transformIds, (int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero), combined.Height.Value));
            AddInspectorField("Height", combined.Height.ToMillimetres(), "Height", mm => CommitSelectionScaleTo(
                transformIds, combined.Width.Value, (int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)));
            return;
        }

        string? activeId = _editor.Selection.ActiveId;
        DocumentElement? element = doc.Elements.FirstOrDefault(e => e.Id == activeId);
        if (element is null) return;

        AddInspectorRow("Type", element.ElementType, false);
        AddInspectorTextField("Name", element.Name ?? string.Empty, value =>
            CommitMetadata([element.Id], metadata => metadata with { Name = string.IsNullOrWhiteSpace(value) ? null : value.Trim() }));
        AddInspectorMetadataChecks([element]);
        AddInspectorSeparator();

        AddInspectorField("X", element.Bounds.X.ToMillimetres(), "X", mm => CommitInspectorBounds(
            new(new((int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)), element.Bounds.Y,
                element.Bounds.Width, element.Bounds.Height)));
        AddInspectorField("Y", element.Bounds.Y.ToMillimetres(), "Y", mm => CommitInspectorBounds(
            new(element.Bounds.X, new((int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)),
                element.Bounds.Width, element.Bounds.Height)));
        AddInspectorField("Width", element.Bounds.Width.ToMillimetres(), "Width", mm => CommitInspectorBounds(
            new(element.Bounds.X, element.Bounds.Y,
                new((int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)), element.Bounds.Height)));
        AddInspectorField("Height", element.Bounds.Height.ToMillimetres(), "Height", mm => CommitInspectorBounds(
            new(element.Bounds.X, element.Bounds.Y, element.Bounds.Width,
                new((int)Math.Round(mm * 1000, MidpointRounding.AwayFromZero)))));

        AddInspectorSeparator();

        switch (element)
        {
            case RectangleElement rect:
                AddInspectorRow("Ink", rect.Ink.ToString(), true);
                AddInspectorRow("Fill", rect.Fill.ToString(), true);
                break;
            case LineElement line:
                AddInspectorRow("Ink", line.Ink.ToString(), true);
                AddInspectorRow("Thickness", $"{line.Thickness.ToMillimetres():F2} mm", false);
                break;
            case TextElement text:
                AddInspectorRow("Ink", text.Ink.ToString(), true);
                AddInspectorTextField("Text", text.Text, value =>
                    CommitTextProperty(text.Id, "text", text.Text, value));
                AddInspectorField("Font Size", text.FontSizePoints, "FontSize", pt =>
                    CommitTextProperty(text.Id, "fontSizePoints", text.FontSizePoints, (int)Math.Round(pt, MidpointRounding.AwayFromZero)));
                AddInspectorTextField("Font", text.FontFamily ?? "", value =>
                    CommitTextProperty(text.Id, "fontFamily", text.FontFamily ?? "", (object)(string.IsNullOrWhiteSpace(value) ? null : value.Trim())!));
                AddInspectorEnumField("Sizing", text.FrameSizing, value =>
                    CommitTextProperty(text.Id, "frameSizing", text.FrameSizing, value));
                AddInspectorEnumField("Wrapping", text.Wrapping, value =>
                    CommitTextProperty(text.Id, "wrapping", text.Wrapping, value));
                AddInspectorEnumField("Overflow", text.Overflow, value =>
                    CommitTextProperty(text.Id, "overflow", text.Overflow, value));
                AddInspectorEnumField("H Align", text.HorizontalAlignment, value =>
                    CommitTextProperty(text.Id, "horizontalAlignment", text.HorizontalAlignment, value));
                AddInspectorEnumField("V Align", text.VerticalAlignment, value =>
                    CommitTextProperty(text.Id, "verticalAlignment", text.VerticalAlignment, value));
                AddInspectorField("Rotation", text.RotationMillidegrees / 1000.0, "Rotation", degrees =>
                    CommitTextProperty(
                        text.Id,
                        "rotationMillidegrees",
                        text.RotationMillidegrees,
                        ElementGeometry.NormalizeAngle((int)Math.Round(degrees * 1000, MidpointRounding.AwayFromZero))));
                break;
            case ImageElement img:
                AddInspectorRow("Ink", img.Ink.ToString(), true);
                AddInspectorRow("Asset", img.AssetId, false);
                break;
        }
    }

    private void AddDocumentInspector()
    {
        LabelDocument document = _editor.Session.Document;
        AddInspectorRow("Media", document.MediaProfileId, false);
        AddInspectorRow("Width", $"{document.PageDimensions.Width.ToMillimetres():0.0} mm (fixed)", false);
        if (document.MediaKind == DocumentMediaKind.Continuous)
        {
            AddInspectorField("Length", document.PageDimensions.Height.ToMillimetres(), "Length", millimetres =>
            {
                MediaProfile media = MediaCatalog.CreateBuiltIn().Get(document.MediaProfileId);
                double minimum = media.Cutter.MinimumContinuousLength?.ToMillimetres() ?? 0.001;
                double maximum = media.Cutter.MaximumContinuousLength?.ToMillimetres() ?? int.MaxValue / 1000.0;
                double clamped = Math.Clamp(millimetres, minimum, maximum);
                Micrometre length = Micrometre.FromMillimetres(clamped);
                if (length == _editor.Session.Document.PageDimensions.Height) return;
                _editor.Session.ExecuteCommand(new ChangePageLengthCommand(_editor.Session.Document, length));
            });
        }
        else
        {
            AddInspectorRow("Length", $"{document.PageDimensions.Height.ToMillimetres():0.0} mm (fixed)", false);
        }

        AddInspectorField("Safe inset", document.DesignMetadata.SafeMargins.Left.ToMillimetres(), "SafeMargin", millimetres =>
        {
            LabelDocument current = _editor.Session.Document;
            Micrometre margin = Micrometre.FromMillimetres(Math.Max(0, millimetres));
            DocumentDesignMetadata metadata = new(
                current.DesignMetadata.Groups,
                current.DesignMetadata.Guides,
                current.DesignMetadata.Grid,
                DocumentSafeMargins.Uniform(margin));
            _editor.Session.ExecuteCommand(new ChangeDesignMetadataCommand(current.DesignMetadata, metadata));
        });
        AddInspectorRow("Grid", $"{document.DesignMetadata.Grid.XSpacing.ToMillimetres():0.###} mm", false);
    }

    private void AddInspectorRow(string label, string value, bool isEditable)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });
        panel.Children.Add(new TextBlock { Text = value, Foreground = (Brush)FindResource("TextBrush"), FontSize = 12 });
        InspectorPanel.Children.Add(panel);
    }

    private void AddInspectorEnumField<T>(string label, T value, Action<T> onCommit)
        where T : struct, Enum
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });
        var comboBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<T>(),
            SelectedItem = value,
            MinWidth = 112,
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is T selected && !EqualityComparer<T>.Default.Equals(selected, value))
            {
                onCommit(selected);
            }
        };
        panel.Children.Add(comboBox);
        InspectorPanel.Children.Add(panel);
    }

    private void AddInspectorField(string label, double value, string propertyName, Action<double> onCommit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });

        var textBox = new TextBox { Style = (Style)FindResource("InspectorField") };
        textBox.Text = value.ToString("F1");
        double originalValue = value;

        textBox.GotFocus += (_, _) => originalValue = double.TryParse(textBox.Text, out double v) ? v : value;
        textBox.LostFocus += (_, _) =>
        {
            if (double.TryParse(textBox.Text, out double newValue))
            {
                if (Math.Abs(newValue - originalValue) > 0.001)
                    onCommit(newValue);
            }
            else
            {
                textBox.Text = originalValue.ToString("F1");
            }
        };
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (double.TryParse(textBox.Text, out double newValue))
                {
                    if (Math.Abs(newValue - originalValue) > 0.001)
                        onCommit(newValue);
                }
                else
                {
                    textBox.Text = originalValue.ToString("F1");
                }
                Keyboard.ClearFocus();
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = originalValue.ToString("F1");
                Keyboard.ClearFocus();
            }
        };

        panel.Children.Add(textBox);
        InspectorPanel.Children.Add(panel);
    }

    private void AddInspectorTextField(string label, string value, Action<string> onCommit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });
        var textBox = new TextBox { Style = (Style)FindResource("InspectorField"), Text = value };
        string originalValue = value;
        textBox.LostFocus += (_, _) =>
        {
            if (!string.Equals(textBox.Text, originalValue, StringComparison.Ordinal))
            {
                onCommit(textBox.Text);
            }
        };
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
            }
            else if (e.Key == Key.Escape)
            {
                textBox.Text = originalValue;
                Keyboard.ClearFocus();
            }
        };
        panel.Children.Add(textBox);
        InspectorPanel.Children.Add(panel);
    }

    private void AddInspectorMetadataChecks(IReadOnlyList<DocumentElement> elements)
    {
        bool? visible = CommonValue(elements.Select(element => element.IsVisible));
        bool? locked = CommonValue(elements.Select(element => element.IsLocked));
        AddInspectorCheck("Visible", visible, value =>
            CommitMetadata(elements.Select(element => element.Id), metadata => metadata with { IsVisible = value }));
        AddInspectorCheck("Locked", locked, value =>
            CommitMetadata(elements.Select(element => element.Id), metadata => metadata with { IsLocked = value }));
    }

    private void AddInspectorCheck(string label, bool? value, Action<bool> onCommit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });
        var checkBox = new CheckBox { IsChecked = value, IsThreeState = value is null, VerticalAlignment = VerticalAlignment.Center };
        checkBox.Click += (_, _) => onCommit(checkBox.IsChecked ?? true);
        panel.Children.Add(checkBox);
        InspectorPanel.Children.Add(panel);
    }

    private static bool? CommonValue(IEnumerable<bool> values)
    {
        bool[] all = values.ToArray();
        return all.All(value => value) ? true : all.All(value => !value) ? false : null;
    }

    private void AddInspectorSeparator()
    {
        InspectorPanel.Children.Add(new Separator
        {
            Margin = new Thickness(0, 6, 0, 6),
            Background = (Brush)FindResource("DividerBrush"),
        });
    }

    private void CommitInspectorBounds(MicrometreRect newBounds)
    {
        string? activeId = _editor.Selection.ActiveId;
        if (activeId is null) return;

        DocumentElement? element = _editor.Session.Document.Elements.FirstOrDefault(e => e.Id == activeId);
        if (element is null) return;

        if (_editor.Session.Document.IsEffectivelyLocked(activeId)) return;

        _editor.Session.ExecuteCommand(new ResizeElementCommand(activeId, element.Bounds, newBounds));
    }

    private void CommitTextProperty(string elementId, string propertyName, object oldValue, object newValue)
    {
        if (Equals(oldValue, newValue)) return;

        TextElement? before = _editor.Session.Document.Elements
            .OfType<TextElement>()
            .FirstOrDefault(element => element.Id == elementId);
        if (before is null) return;

        TextElement changed = propertyName switch
        {
            "text" => before with { Text = (string)newValue },
            "fontSizePoints" => before with { FontSizePoints = (int)newValue },
            "fontFamily" => before with { FontFamily = (string?)newValue },
            "frameSizing" => before with { FrameSizing = (TextFrameSizingMode)newValue },
            "wrapping" => before with { Wrapping = (TextWrappingMode)newValue },
            "overflow" => before with { Overflow = (TextOverflowMode)newValue },
            "horizontalAlignment" => before with { HorizontalAlignment = (TextHorizontalAlignment)newValue },
            "verticalAlignment" => before with { VerticalAlignment = (TextVerticalAlignment)newValue },
            "rotationMillidegrees" => before with { RotationMillidegrees = (int)newValue },
            _ => before,
        };
        TextElement after = TextLayoutEngine.ResolveAutomaticFrame(changed);
        if (before == after) return;
        _editor.Session.ExecuteCommand(new ReplaceElementsCommand([(before, after)]));
    }

    private void CommitSelectionMoveTo(List<string> elementIds, int targetX, int targetY)
    {
        LabelDocument doc = _editor.Session.Document;
        if (SelectionBounds.ContainsLockedMember(doc, elementIds)) return;

        ElementTransform[] transforms = SelectionTransformService.PlanMoveTo(doc, elementIds, targetX, targetY);
        _editor.Session.ExecuteCommand(SelectionTransformService.ToCommand(transforms));
    }

    private void CommitSelectionScaleTo(List<string> elementIds, int targetWidth, int targetHeight)
    {
        LabelDocument doc = _editor.Session.Document;
        if (SelectionBounds.ContainsLockedMember(doc, elementIds)) return;

        targetWidth = Math.Max(SelectionTransformService.MinElementWidthMicrometres, targetWidth);
        targetHeight = Math.Max(SelectionTransformService.MinElementHeightMicrometres, targetHeight);

        ElementTransform[] transforms = SelectionTransformService.PlanScaleToSize(doc, elementIds, targetWidth, targetHeight);
        _editor.Session.ExecuteCommand(SelectionTransformService.ToCommand(transforms));
    }

    private void CommitGroupName(string groupId, string name)
    {
        LabelDocument doc = _editor.Session.Document;
        ElementGroup? group = doc.DesignMetadata.Groups
            .FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
        if (group is null) return;

        string? newName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (string.Equals(group.Name, newName, StringComparison.Ordinal)) return;

        ElementGroup updated = new ElementGroup(group.Id, newName, group.MemberIds, group.IsVisible, group.IsLocked);

        List<ElementGroup> groups = doc.DesignMetadata.Groups
            .Select(g => string.Equals(g.Id, groupId, StringComparison.Ordinal) ? updated : g)
            .ToList();
        DocumentDesignMetadata newMetadata = new(
            groups, doc.DesignMetadata.Guides, doc.DesignMetadata.Grid, doc.DesignMetadata.SafeMargins);

        _editor.Session.ExecuteCommand(new ChangeDesignMetadataCommand(doc.DesignMetadata, newMetadata));
    }

    private void CommitGroupMetadata(string groupId, ElementGroup current, bool? visible = null, bool? locked = null)
    {
        LabelDocument doc = _editor.Session.Document;
        ElementGroup? group = doc.DesignMetadata.Groups
            .FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
        if (group is null) return;

        ElementGroup updated = new ElementGroup(
            group.Id,
            group.Name,
            group.MemberIds,
            visible ?? group.IsVisible,
            locked ?? group.IsLocked);

        if (updated.IsVisible == group.IsVisible && updated.IsLocked == group.IsLocked) return;

        List<ElementGroup> groups = doc.DesignMetadata.Groups
            .Select(g => string.Equals(g.Id, groupId, StringComparison.Ordinal) ? updated : g)
            .ToList();
        DocumentDesignMetadata newMetadata = new(
            groups, doc.DesignMetadata.Guides, doc.DesignMetadata.Grid, doc.DesignMetadata.SafeMargins);

        _editor.Session.ExecuteCommand(new ChangeDesignMetadataCommand(doc.DesignMetadata, newMetadata));
    }

    private void CommitMetadata(IEnumerable<string> elementIds, Func<ElementMetadata, ElementMetadata> update)
    {
        HashSet<string> ids = elementIds.ToHashSet(StringComparer.Ordinal);
        ElementMetadataChange[] changes = _editor.Session.Document.Elements
            .Where(element => ids.Contains(element.Id))
            .Select(element =>
            {
                ElementMetadata before = ElementMetadata.From(element);
                return new ElementMetadataChange(element.Id, before, update(before));
            })
            .Where(change => change.Before != change.After)
            .ToArray();
        if (changes.Length > 0)
        {
            _editor.Session.ExecuteCommand(new ChangeElementMetadataCommand(changes));
        }
    }

    // Layers
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);

    private void UpdateLayers()
    {
        _updatingLayers = true;
        LayersList.Items.Clear();

        LabelDocument doc = _editor.Session.Document;

        Dictionary<string, int> typeCounts = new(StringComparer.Ordinal);
        Dictionary<string, string> fallbackNames = new(StringComparer.Ordinal);
        foreach (DocumentElement element in doc.Elements)
        {
            string typeName = GetElementTypeName(element);
            typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName) + 1;
            fallbackNames[element.Id] = $"{typeName} {typeCounts[typeName]}";
        }

        Dictionary<string, ElementGroup> groupByMember = new(StringComparer.Ordinal);
        foreach (ElementGroup group in doc.DesignMetadata.Groups)
        {
            foreach (string memberId in group.MemberIds)
            {
                groupByMember[memberId] = group;
            }
        }

        HashSet<string> renderedGroupIds = new(StringComparer.Ordinal);
        for (int i = doc.Elements.Count - 1; i >= 0; i--)
        {
            DocumentElement element = doc.Elements[i];
            if (groupByMember.TryGetValue(element.Id, out ElementGroup? group))
            {
                if (renderedGroupIds.Contains(group.Id)) continue;
                renderedGroupIds.Add(group.Id);

                bool collapsed = _collapsedGroups.Contains(group.Id);
                var groupRow = CreateGroupRow(doc, group, collapsed);
                LayersList.Items.Add(groupRow);

                if (!collapsed)
                {
                    foreach (string memberId in group.MemberIds)
                    {
                        DocumentElement? member = doc.Elements.FirstOrDefault(e => e.Id == memberId);
                        if (member is null) continue;
                        ListBoxItem memberRow = CreateMemberRow(doc, member, fallbackNames, group.Id);
                        LayersList.Items.Add(memberRow);
                    }
                }
            }
            else
            {
                ListBoxItem row = CreateStandaloneRow(doc, element, fallbackNames);
                LayersList.Items.Add(row);
            }
        }

        UpdateLayersSelectionCore();
        _updatingLayers = false;
    }

    private ListBoxItem CreateGroupRow(LabelDocument doc, ElementGroup group, bool collapsed)
    {
        string displayName = string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name;
        var row = new Grid { Margin = new Thickness(2), Tag = group.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var expandBtn = new Button
        {
            Content = collapsed ? "+" : "-",
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 4, 0),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 14,
            Padding = new Thickness(0),
        };
        expandBtn.Click += (_, _) =>
        {
            if (_collapsedGroups.Contains(group.Id))
                _collapsedGroups.Remove(group.Id);
            else
                _collapsedGroups.Add(group.Id);
            UpdateLayers();
        };
        Grid.SetColumn(expandBtn, 0);
        row.Children.Add(expandBtn);

        var visible = new CheckBox
        {
            Content = "V",
            IsChecked = group.IsVisible,
            ToolTip = "Group Visible",
            Margin = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        visible.Click += (_, _) => CommitGroupMetadata(group.Id, group with { }, visible: visible.IsChecked == true);
        Grid.SetColumn(visible, 1);
        row.Children.Add(visible);

        var locked = new CheckBox
        {
            Content = "L",
            IsChecked = group.IsLocked,
            ToolTip = "Group Locked",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        locked.Click += (_, _) => CommitGroupMetadata(group.Id, group with { }, locked: locked.IsChecked == true);
        Grid.SetColumn(locked, 2);
        row.Children.Add(locked);

        var label = new TextBlock
        {
            Text = $"{displayName} ({group.MemberIds.Count})",
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 3);
        row.Children.Add(label);

        return new ListBoxItem { Content = row, Tag = group.Id };
    }

    private ListBoxItem CreateMemberRow(LabelDocument doc, DocumentElement element, Dictionary<string, string> fallbackNames, string groupId)
    {
        var row = new Grid { Margin = new Thickness(24, 2, 2, 2), Tag = element.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var visible = new CheckBox
        {
            Content = "V",
            IsChecked = element.IsVisible,
            ToolTip = "Visible",
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        visible.Click += (_, _) => CommitMetadata([element.Id], metadata => metadata with { IsVisible = visible.IsChecked == true });
        Grid.SetColumn(visible, 0);
        row.Children.Add(visible);

        var locked = new CheckBox
        {
            Content = "L",
            IsChecked = element.IsLocked,
            ToolTip = "Locked",
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        locked.Click += (_, _) => CommitMetadata([element.Id], metadata => metadata with { IsLocked = locked.IsChecked == true });
        Grid.SetColumn(locked, 1);
        row.Children.Add(locked);

        string displayName = string.IsNullOrWhiteSpace(element.Name) ? fallbackNames[element.Id] : element.Name;
        var label = new TextBlock
        {
            Text = $"{displayName} ({element.ElementType})",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 2);
        row.Children.Add(label);

        return new ListBoxItem { Content = row, Tag = element.Id, IsHitTestVisible = true };
    }

    private ListBoxItem CreateStandaloneRow(LabelDocument doc, DocumentElement element, Dictionary<string, string> fallbackNames)
    {
        var row = new Grid { Margin = new Thickness(2), Tag = element.Id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var visible = new CheckBox
        {
            Content = "V",
            IsChecked = element.IsVisible,
            ToolTip = "Visible",
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        visible.Click += (_, _) => CommitMetadata([element.Id], metadata => metadata with { IsVisible = visible.IsChecked == true });
        Grid.SetColumn(visible, 0);
        row.Children.Add(visible);

        var locked = new CheckBox
        {
            Content = "L",
            IsChecked = element.IsLocked,
            ToolTip = "Locked",
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        locked.Click += (_, _) => CommitMetadata([element.Id], metadata => metadata with { IsLocked = locked.IsChecked == true });
        Grid.SetColumn(locked, 1);
        row.Children.Add(locked);

        string displayName = string.IsNullOrWhiteSpace(element.Name) ? fallbackNames[element.Id] : element.Name;
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = (Brush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        labels.Children.Add(new TextBlock
        {
            Text = element.ElementType,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
        });
        Grid.SetColumn(labels, 2);
        row.Children.Add(labels);

        return new ListBoxItem { Content = row, Tag = element.Id };
    }

    private void UpdateLayersSelection()
    {
        _updatingLayers = true;
        UpdateLayersSelectionCore();
        _updatingLayers = false;
    }

    private void UpdateLayersSelectionCore()
    {
        LabelDocument doc = _editor.Session.Document;
        foreach (ListBoxItem item in LayersList.Items)
        {
            if (item.Tag is string id)
            {
                bool isGroupRow = doc.DesignMetadata.Groups.Any(g => string.Equals(g.Id, id, StringComparison.Ordinal));
                item.IsSelected = isGroupRow
                    ? _editor.Selection.ContainsGroup(id)
                    : _editor.Selection.ContainsElement(id) && !_editor.Selection.IsGroupMember(doc, id);
            }
        }
    }

    private void OnLayersSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLayers) return;

        string[] selectedIds = LayersList.SelectedItems.Cast<ListBoxItem>()
            .Select(item => item.Tag as string)
            .OfType<string>()
            .ToArray();
        CommitTextEdit();

        LabelDocument doc = _editor.Session.Document;
        List<SelectionTarget> targets = new();

        foreach (string id in selectedIds)
        {
            bool isGroup = doc.DesignMetadata.Groups.Any(g => string.Equals(g.Id, id, StringComparison.Ordinal));
            if (isGroup)
            {
                targets.Add(SelectionTarget.Group(id));
            }
            else
            {
                string? groupId = doc.FindGroupIdForMember(id);
                if (groupId is not null)
                {
                    if (!targets.Any(t => t is SelectionTarget.GroupTarget g && g.GroupId == groupId))
                    {
                        targets.Add(SelectionTarget.Group(groupId));
                    }
                }
                else
                {
                    targets.Add(SelectionTarget.Element(id));
                }
            }
        }

        _editor.Selection.SetSelection(targets.Distinct());
    }

    private void OnGroup(object sender, RoutedEventArgs e)
    {
        if (!_editor.Selection.HasSelection) return;
        LabelDocument doc = _editor.Session.Document;
        IReadOnlyCollection<string> elementIds = _editor.Selection.GetSelectedElementIds(doc);
        if (elementIds.Count < 2) return;

        var cmd = new GroupElementsCommand(elementIds, doc);
        _editor.Session.ExecuteCommand(cmd);
        _editor.Selection.SelectGroup(cmd.GroupId);
    }

    private void OnUngroup(object sender, RoutedEventArgs e)
    {
        if (_editor.Selection.ActiveGroupId is null) return;
        LabelDocument doc = _editor.Session.Document;
        string groupId = _editor.Selection.ActiveGroupId;

        ElementGroup? group = doc.DesignMetadata.Groups
            .FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
        if (group is null) return;

        _editor.Session.ExecuteCommand(new UngroupElementsCommand(groupId, doc));
        _editor.Selection.SetElementSelection(group.MemberIds);
    }

    private void OnBringForward(object sender, RoutedEventArgs e) => ExecuteZOrder(
        (ids, document) => new BringForwardCommand(ids, document));

    private void OnSendBackward(object sender, RoutedEventArgs e) => ExecuteZOrder(
        (ids, document) => new SendBackwardCommand(ids, document));

    private void OnBringToFront(object sender, RoutedEventArgs e) => ExecuteZOrder(
        (ids, document) => new BringToFrontCommand(ids, document));

    private void OnSendToBack(object sender, RoutedEventArgs e) => ExecuteZOrder(
        (ids, document) => new SendToBackCommand(ids, document));

    private void ExecuteZOrder(Func<IEnumerable<string>, LabelDocument, IEditorCommand> createCommand)
    {
        if (!_editor.Selection.HasSelection) return;
        LabelDocument doc = _editor.Session.Document;
        List<string> ids = new();

        foreach (SelectionTarget target in _editor.Selection.Targets)
        {
            switch (target)
            {
                case SelectionTarget.GroupTarget grp:
                    ElementGroup? group = doc.DesignMetadata.Groups
                        .FirstOrDefault(g => string.Equals(g.Id, grp.GroupId, StringComparison.Ordinal));
                    if (group is not null)
                    {
                        ids.AddRange(group.MemberIds);
                    }
                    break;
                case SelectionTarget.ElementTarget elem:
                    string? groupId = doc.FindGroupIdForMember(elem.ElementId);
                    if (groupId is not null)
                    {
                        ElementGroup? g = doc.DesignMetadata.Groups
                            .FirstOrDefault(gg => string.Equals(gg.Id, groupId, StringComparison.Ordinal));
                        if (g is not null && !ids.Contains(g.Id))
                        {
                            ids.AddRange(g.MemberIds);
                        }
                    }
                    else
                    {
                        ids.Add(elem.ElementId);
                    }
                    break;
            }
        }

        if (ids.Count > 0)
        {
            _editor.Session.ExecuteCommand(createCommand(ids.Distinct(), doc));
        }
    }

    private static string GetElementTypeName(DocumentElement element) => element switch
    {
        RectangleElement => "Rectangle",
        LineElement => "Line",
        TextElement => "Text",
        ImageElement => "Image",
        _ => "Element",
    };

    // Clipboard
    private const string ClipboardDataFormat = "LabelStudioClipboard";

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!_editor.Selection.HasSelection) return;
        LabelDocument doc = _editor.Session.Document;

        Func<string, byte[]?> assetProvider = _documentAssets is not null
            ? id => _documentAssets.TryGetValue(id, out byte[]? bytes) ? bytes : null
            : _ => null;

        ClipboardPayload payload = _cloneService.BuildPayload(doc, _editor.Selection.Targets, assetProvider);
        _internalClipboard = payload;
        _cloneService.ResetPasteOffset();

        try
        {
            string json = ClipboardSerializer.Serialize(payload);
            System.Windows.Clipboard.SetData(ClipboardDataFormat, json);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Clipboard copy warning: {ex.Message}";
        }
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        ClipboardPayload? payload = TryGetClipboardPayload();
        if (payload is null) return;

        LabelDocument doc = _editor.Session.Document;

        Func<string, byte[]?> destAssetProvider = _documentAssets is not null
            ? id => _documentAssets.TryGetValue(id, out byte[]? bytes) ? bytes : null
            : _ => null;

        Func<string, string, byte[], string> assetImporter = (sourceId, hash, bytes) =>
        {
            string newId = Guid.NewGuid().ToString("N");
            _documentAssets ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _documentAssets[newId] = bytes;
            return newId;
        };

        PastePlan plan = _cloneService.PlanPaste(payload, doc, destAssetProvider, assetImporter);
        _cloneService.IncrementPasteCount();

        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        _editor.Session.ExecuteCommand(cmd);
        _editor.Selection.SetSelection(plan.NewSelectionTargets);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (!_editor.Selection.HasSelection) return;
        LabelDocument doc = _editor.Session.Document;

        Func<string, byte[]?> assetProvider = _documentAssets is not null
            ? id => _documentAssets.TryGetValue(id, out byte[]? bytes) ? bytes : null
            : _ => null;

        ClipboardPayload payload = _cloneService.BuildPayload(doc, _editor.Selection.Targets, assetProvider);
        _internalClipboard = payload;
        _cloneService.ResetPasteOffset();

        Func<string, byte[]?> destAssetProvider = assetProvider;
        Func<string, string, byte[], string> assetImporter = (sourceId, hash, bytes) =>
        {
            string newId = Guid.NewGuid().ToString("N");
            _documentAssets ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _documentAssets[newId] = bytes;
            return newId;
        };

        PastePlan plan = _cloneService.PlanPaste(payload, doc, destAssetProvider, assetImporter);
        _cloneService.IncrementPasteCount();

        PasteElementsCommand cmd = new(plan.NewElements, plan.NewGroups, doc);
        _editor.Session.ExecuteCommand(cmd);
        _editor.Selection.SetSelection(plan.NewSelectionTargets);
    }

    private ClipboardPayload? TryGetClipboardPayload()
    {
        if (_internalClipboard is not null)
        {
            return _internalClipboard;
        }

        try
        {
            if (System.Windows.Clipboard.ContainsData(ClipboardDataFormat))
            {
                string? json = System.Windows.Clipboard.GetData(ClipboardDataFormat) as string;
                if (json is not null)
                {
                    return ClipboardSerializer.Deserialize(json);
                }
            }
        }
        catch { }

        return null;
    }
}
