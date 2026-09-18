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
        _editor.Session.DocumentChanged += (_, _) => { InvalidateCanvas(); UpdateInspector(); UpdateTitle(); };
        _editor.Session.DirtyChanged += (_, _) => { UpdateTitle(); UpdateCommandButtons(); };
        _editor.Selection.SelectionChanged += (_, _) => { InvalidateCanvas(); UpdateInspector(); };
        _editor.ViewTransform.Reset();
        UpdateInspector();
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

        if (!_editor.Selection.HasSelection || _editor.Selection.ActiveId is null)
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

        string? activeId = _editor.Selection.ActiveId;
        DocumentElement? element = _editor.Session.Document.Elements.FirstOrDefault(e => e.Id == activeId);
        if (element is null) return;

        AddInspectorRow("Type", element.ElementType, false);
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

        _editor.Session.ExecuteCommand(new ResizeElementCommand(activeId, element.Bounds, newBounds));
    }
}