using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Clipboard;
using LabelStudio.Editor.Selection;
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
    private readonly SelectionCloneService _cloneService = new();
    private ClipboardPayload? _internalClipboard;
    private Dictionary<string, byte[]>? _documentAssets;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateNewDocument("brother.dk-22251");
        UpdateCommandButtons();
    }

    private void CreateNewDocument(string mediaProfileId)
    {
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        MediaProfile media = catalog.Get(mediaProfileId);

        PhysicalSize dims = media.Kind == MediaKind.Continuous
            ? new(new(media.PhysicalWidthMicrometres), Micrometre.Zero)
            : new(new(media.PhysicalWidthMicrometres), new(media.PhysicalLengthMicrometres ?? 0));

        MicrometreRect printable = media.Kind == MediaKind.Continuous
            ? new(new(media.PrintableArea.CrossFeedOffset.Value), Micrometre.Zero,
                  media.PrintableArea.Width, Micrometre.Zero)
            : new(media.PrintableArea.CrossFeedOffset, media.PrintableArea.FeedOffset,
                  media.PrintableArea.Width,
                  media.PrintableArea.Length.HasValue ? media.PrintableArea.Length.Value : Micrometre.Zero);

        MediaSnapshot snap = new(mediaProfileId, dims, printable);
        LabelDocument doc = LabelDocument.Create(dims, mediaProfileId, snap);

        _editor = new EditorState(doc);
        _input = new CanvasInputHandler(_editor, _painter, InvalidateCanvas);
        _editor.Session.DocumentChanged += (_, _) => { InvalidateCanvas(); UpdateInspector(); UpdateLayers(); UpdateTitle(); };
        _editor.Session.DirtyChanged += (_, _) => { UpdateTitle(); UpdateCommandButtons(); };
        _editor.Selection.SelectionChanged += (_, _) => { InvalidateCanvas(); UpdateInspector(); UpdateLayersSelection(); };
        _editor.ViewTransform.Reset();
        UpdateInspector();
        UpdateLayers();
        UpdateTitle();
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
        _input.Paint(e, scale);
    }

    // Canvas input
    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e) => _input.OnMouseDown(sender, e);
    private void OnCanvasMouseMove(object sender, MouseEventArgs e) => _input.OnMouseMove(sender, e);
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => _input.OnMouseUp(sender, e);
    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e) { _input.OnMouseWheel(sender, e); UpdateCommandButtons(); }

    // Window keyboard
    private void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
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

        if (Keyboard.Modifiers == ModifierKeys.Control)
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
        if (!PromptSaveIfDirty()) return;
        var dialog = new NewLabelDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            CreateNewDocument(dialog.SelectedMediaProfileId);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs? e)
    {
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
        _editor.Session.Undo();
        _editor.Selection.PruneDeleted(_editor.Session.Document);
        UpdateCommandButtons();
        InvalidateCanvas();
    }

    private void OnRedo(object sender, RoutedEventArgs? e)
    {
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
        InvalidateCanvas();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _editor.ViewTransform.Zoom = Math.Max(CanvasTransform.MinZoom, _editor.ViewTransform.Zoom / 1.25);
        UpdateCommandButtons();
        InvalidateCanvas();
    }

    private void OnZoomFit(object sender, RoutedEventArgs e)
    {
        PhysicalSize dims = _editor.Session.Document.PageDimensions;
        double labelW = dims.Width.Value / 1000.0 * CanvasTransform.BaseDipsPerMm;
        double labelH = (dims.Height > Micrometre.Zero ? dims.Height : new Micrometre(200_000)).Value / 1000.0 * CanvasTransform.BaseDipsPerMm;
        double availW = CanvasHost.ActualWidth - 80;
        double availH = CanvasHost.ActualHeight - 80;
        double zoomX = availW / labelW;
        double zoomY = availH / labelH;
        _editor.ViewTransform.Zoom = Math.Min(zoomX, zoomY);
        _editor.ViewTransform.OffsetX = (CanvasHost.ActualWidth - labelW * _editor.ViewTransform.Zoom) / 2;
        _editor.ViewTransform.OffsetY = (CanvasHost.ActualHeight - labelH * _editor.ViewTransform.Zoom) / 2;
        UpdateCommandButtons();
        InvalidateCanvas();
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
        _editor.ActiveTool = tool;
        UpdateToolButtons();
        InvalidateCanvas();
    }

    private void UpdateToolButtons()
    {
        foreach (UIElement? child in ((StackPanel)ToolSelect.Parent).Children)
        {
            if (child is Button btn) btn.Background = System.Windows.Media.Brushes.Transparent;
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
        if (activeBtn is not null) activeBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC));
    }

    // Thermal preview
    private void OnThermalPreview(object sender, RoutedEventArgs e)
    {
        LabelDocument doc = _editor.Session.Document;
        PreparedScene scene = new LayoutEngine().Prepare(doc);
        MediaCatalog catalog = MediaCatalog.CreateBuiltIn();
        if (!catalog.TryGet(doc.MediaProfileId, out MediaProfile? media) || media is null) return;

        int headLeft = media.Kind == MediaKind.DieCut ? 555 : 12;
        int printableWidth = media.Kind == MediaKind.DieCut ? 165 : 696;
        int rasterHeight = media.Kind == MediaKind.DieCut ? 566 : 500;

        RenderTarget target = new(
            300, 300, 720, rasterHeight,
            doc.MediaGeometry.PrintableArea,
            headLeft, printableWidth,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)]);

        RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
        var preview = new ThermalPreviewWindow(planes) { Owner = this };
        preview.ShowDialog();
    }

    // Mock print
    private void OnMockPrint(object sender, RoutedEventArgs e)
    {
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

        int headLeft = media.Kind == MediaKind.DieCut ? 555 : 12;
        int printableWidth = media.Kind == MediaKind.DieCut ? 165 : 696;
        int rasterHeight = media.Kind == MediaKind.DieCut ? 566 : 500;

        RenderTarget target = new(
            settings.Dpi, settings.Dpi, 720, rasterHeight,
            doc.MediaGeometry.PrintableArea,
            headLeft, printableWidth,
            [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)]);

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
        var dialog = new PrintDialogWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            string queueName = dialog.PrinterQueueName;
            if (string.IsNullOrEmpty(queueName))
            {
                MessageBox.Show("No printer queue specified.", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

            int headLeft = media.Kind == MediaKind.DieCut ? 555 : 12;
            int printableWidth = media.Kind == MediaKind.DieCut ? 165 : 696;
            int rasterHeight = media.Kind == MediaKind.DieCut ? 566 : 500;

            RenderTarget target = new(
                settings.Dpi, settings.Dpi, 720, rasterHeight,
                doc.MediaGeometry.PrintableArea,
                headLeft, printableWidth,
                [new InkOutputChannel("Black", false), new InkOutputChannel("Red", media.SupportsRed)]);

            RenderedPlanes planes = new ThermalTargetRenderer().Render(scene, target);
            PrintIntent intent = new(doc.Id, scene, media, settings);
            DevicePrintJob job = new(intent, planes, media, settings, $"Physical-{doc.Id}");

            try
            {
                Ql800PrinterBackend backend = new(
                    transport: new WindowsRawTransport(),
                    transportTarget: new PrinterTransportTarget(queueName));
                PrintResult result = backend.Print(job);

                if (result.Success)
                    StatusLabel.Text = $"Print job sent to {queueName}";
                else
                    MessageBox.Show(result.Error ?? "Unknown error", "Print Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Closing
    private void MainWindow_OnClosing(object sender, CancelEventArgs e)
    {
        if (!PromptSaveIfDirty())
        {
            e.Cancel = true;
        }
    }

    private bool PromptSaveIfDirty()
    {
        if (!_editor.Session.IsDirty) return true;
        var result = MessageBox.Show("Save changes before closing?", "Unsaved Changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            OnSave(this, null!);
            return !_editor.Session.IsDirty;
        }
        return result == MessageBoxResult.No;
    }

    // Inspector
    private void UpdateInspector()
    {
        InspectorPanel.Children.Clear();

        if (!_editor.Selection.HasSelection)
        {
            InspectorPanel.Children.Add(new TextBlock
            {
                Text = "No selection",
                Foreground = System.Windows.Media.Brushes.DimGray,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
            });
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
                AddInspectorRow("Text", text.Text, false);
                AddInspectorRow("Font Size", $"{text.FontSizePoints}pt", false);
                break;
            case ImageElement img:
                AddInspectorRow("Ink", img.Ink.ToString(), true);
                AddInspectorRow("Asset", img.AssetId, false);
                break;
        }
    }

    private void AddInspectorRow(string label, string value, bool isEditable)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("InspectorLabel"), Width = 60 });
        panel.Children.Add(new TextBlock { Text = value, Foreground = System.Windows.Media.Brushes.White, FontSize = 12 });
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
            Background = System.Windows.Media.Brushes.DimGray,
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
        DocumentDesignMetadata newMetadata = new(groups, doc.DesignMetadata.Guides, doc.DesignMetadata.Grid);

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
        DocumentDesignMetadata newMetadata = new(groups, doc.DesignMetadata.Guides, doc.DesignMetadata.Grid);

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
            Foreground = System.Windows.Media.Brushes.LightGray,
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
            Foreground = System.Windows.Media.Brushes.White,
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
            Foreground = System.Windows.Media.Brushes.LightGray,
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
            Foreground = System.Windows.Media.Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        labels.Children.Add(new TextBlock
        {
            Text = element.ElementType,
            Foreground = System.Windows.Media.Brushes.Gray,
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

        LabelDocument doc = _editor.Session.Document;
        List<SelectionTarget> targets = new();

        foreach (ListBoxItem item in LayersList.SelectedItems.Cast<ListBoxItem>())
        {
            if (item.Tag is not string id) continue;

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
