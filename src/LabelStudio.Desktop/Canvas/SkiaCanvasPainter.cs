using System.Windows;
using SkiaSharp;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Snapping;
using LabelStudio.Editor.Transforms;
using LabelStudio.Rendering;
using SelectionTarget = LabelStudio.Editor.Selection.SelectionTarget;

namespace LabelStudio.Desktop.Canvas;

public sealed class SkiaCanvasPainter
{
    private static readonly SKColor WorkspaceColor = new(0xFFE0DDE0);
    private static readonly SKColor LabelColor = new(0xFFFFFFFF);
    private static readonly SKColor PrintableBorderColor = new(0xFF8F7784);
    private static readonly SKColor LabelBorderColor = new(0xFFB8AAB4);
    private static readonly SKColor ElementFillColor = new(0xFF353142);
    private static readonly SKColor ElementStrokeColor = new(0xFF282331);
    private static readonly SKColor SelectionColor = new(0xFFC63F78);
    private static readonly SKColor HandleColor = new(0xFFFFFFFF);
    private static readonly SKColor HandleBorderColor = new(0xFFC63F78);
    private static readonly SKColor HoverColor = new(0x40C63F78u);
    private static readonly SKColor PreviewColor = new(0x66C63F78u);

    public void Paint(
        SKCanvas canvas,
        int surfaceWidth,
        int surfaceHeight,
        float renderScale,
        EditorState editor,
        string? hoverElementId,
        MicrometreRect? creationPreview,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds = null,
        string? editingElementId = null,
        IReadOnlyDictionary<string, int>? previewRotations = null,
        IReadOnlyList<SnapIndicator>? snapIndicators = null,
        Micrometre? previewPageLength = null)
    {
        canvas.Clear(WorkspaceColor);

        LabelDocument doc = editor.Session.Document;
        CanvasTransform view = editor.ViewTransform;

        HashSet<string> selectedElementIds = editor.Selection.GetSelectedElementIds(doc).ToHashSet(StringComparer.Ordinal);

        int savedState = canvas.Save();
        canvas.Scale(renderScale);

        int rotationState = canvas.Save();
        if (view.ViewRotationDegrees != 0)
        {
            (double cos, double sin) = view.RotationMatrix;
            SKMatrix rotation = new(
                (float)cos, (float)-sin, (float)(view.OffsetX + view.RotationOffsetX),
                (float)sin, (float)cos, (float)(view.OffsetY + view.RotationOffsetY),
                0, 0, 1);
            canvas.Concat(rotation);
        }
        else
        {
            canvas.Translate((float)view.OffsetX, (float)view.OffsetY);
        }

        double dipsPerMm = view.DipsPerMm;

        Micrometre pageLength = previewPageLength ?? doc.PageDimensions.Height;
        DrawLabelArea(canvas, doc.PageDimensions.Width, pageLength, dipsPerMm);
        if (editor.ShowPrintLimits)
        {
            DrawPrintableArea(canvas, doc, pageLength, dipsPerMm);
        }
        if (editor.ShowGrid)
        {
            DrawGrid(canvas, doc, pageLength, dipsPerMm);
        }
        if (editor.ShowSafeArea)
        {
            DrawSafeArea(canvas, doc, pageLength, dipsPerMm,
                IsOutsideSafeArea(doc, pageLength, previewBounds, previewRotations));
        }
        DrawElements(
            canvas,
            doc,
            dipsPerMm,
            selectedElementIds,
            previewBounds,
            editingElementId,
            view.ViewRotationDegrees,
            previewRotations);
        DrawHover(canvas, doc, dipsPerMm, hoverElementId, selectedElementIds);

        if (creationPreview is not null)
        {
            DrawCreationPreview(canvas, dipsPerMm, creationPreview.Value);
        }

        canvas.RestoreToCount(rotationState);
        DrawSnapIndicators(canvas, view, snapIndicators);
        DrawCutHandle(canvas, doc, view, pageLength, previewPageLength is not null);
        DrawSelectionAtScreen(canvas, doc, view, selectedElementIds, previewBounds, previewRotations);
        canvas.RestoreToCount(savedState);
    }

    private static double DocToCanvas(Micrometre value, double dipsPerMm) =>
        value.Value / 1000.0 * dipsPerMm;

    private static void DrawLabelArea(SKCanvas canvas, Micrometre width, Micrometre height, double dipsPerMm)
    {
        float x = 0f;
        float y = 0f;
        float w = (float)DocToCanvas(width, dipsPerMm);
        float h = (float)DocToCanvas(height, dipsPerMm);

        using SKPaint paint = new() { Color = LabelColor, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, paint);

        using SKPaint border = new() { Color = LabelBorderColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, border);
    }

    private static void DrawPrintableArea(SKCanvas canvas, LabelDocument doc, Micrometre pageLength, double dipsPerMm)
    {
        MicrometreRect printable = GetPrintableArea(doc, pageLength);
        if (printable.Width <= Micrometre.Zero) return;

        float x = (float)DocToCanvas(printable.X, dipsPerMm);
        float y = (float)DocToCanvas(printable.Y, dipsPerMm);
        float w = (float)DocToCanvas(printable.Width, dipsPerMm);
        float h = (float)DocToCanvas(printable.Height, dipsPerMm);

        using SKPaint border = new() { Color = PrintableBorderColor, IsStroke = true, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, border);
    }

    private static void DrawSafeArea(
        SKCanvas canvas,
        LabelDocument document,
        Micrometre pageLength,
        double dipsPerMm,
        bool emphasized)
    {
        MicrometreRect safe = GetSafeArea(document, pageLength);
        using SKPaint paint = new()
        {
            Color = emphasized ? new SKColor(0xFFD58B24) : new SKColor(0x88918A90),
            IsStroke = true,
            StrokeWidth = emphasized ? 1.5f : 1f,
            PathEffect = SKPathEffect.CreateDash([5, 4], 0),
            IsAntialias = true,
        };
        canvas.DrawRect(
            (float)DocToCanvas(safe.X, dipsPerMm),
            (float)DocToCanvas(safe.Y, dipsPerMm),
            (float)DocToCanvas(safe.Width, dipsPerMm),
            (float)DocToCanvas(safe.Height, dipsPerMm),
            paint);
    }

    private static void DrawGrid(SKCanvas canvas, LabelDocument document, Micrometre pageLength, double dipsPerMm)
    {
        DocumentGridGeometry grid = document.DesignMetadata.Grid;
        DrawGridAxis(canvas, true, grid.Origin.X.Value, grid.XSpacing.Value,
            document.PageDimensions.Width.Value, pageLength.Value, grid.MajorInterval, dipsPerMm);
        DrawGridAxis(canvas, false, grid.Origin.Y.Value, grid.YSpacing.Value,
            pageLength.Value, document.PageDimensions.Width.Value, grid.MajorInterval, dipsPerMm);
    }

    private static void DrawGridAxis(
        SKCanvas canvas,
        bool vertical,
        int origin,
        int spacing,
        int axisLength,
        int perpendicularLength,
        int majorInterval,
        double dipsPerMm)
    {
        if (spacing <= 0 || spacing / 1000.0 * dipsPerMm < 4) return;
        int first = (int)Math.Ceiling((0 - origin) / (double)spacing);
        int last = (int)Math.Floor((axisLength - origin) / (double)spacing);
        using SKPaint minor = new() { Color = new SKColor(0x18766E74), StrokeWidth = 1 };
        using SKPaint major = new() { Color = new SKColor(0x30766E74), StrokeWidth = 1 };
        float perpendicular = (float)(perpendicularLength / 1000.0 * dipsPerMm);
        for (int index = first; index <= last; index++)
        {
            float position = (float)((origin + index * spacing) / 1000.0 * dipsPerMm);
            SKPaint paint = majorInterval > 0 && Math.Abs(index) % majorInterval == 0 ? major : minor;
            if (vertical) canvas.DrawLine(position, 0, position, perpendicular, paint);
            else canvas.DrawLine(0, position, perpendicular, position, paint);
        }
    }

    private static MicrometreRect GetPrintableArea(LabelDocument document, Micrometre pageLength)
    {
        MicrometreRect printable = document.MediaGeometry.PrintableArea;
        if (document.MediaKind != DocumentMediaKind.Continuous) return printable;
        int previousHeight = document.MediaGeometry.PhysicalDimensions.Height.Value;
        int trailing = previousHeight > 0 ? Math.Max(0, previousHeight - printable.Bottom.Value) : printable.Y.Value;
        return printable with { Height = new Micrometre(Math.Max(0, pageLength.Value - printable.Y.Value - trailing)) };
    }

    private static MicrometreRect GetSafeArea(LabelDocument document, Micrometre pageLength)
    {
        MicrometreRect printable = GetPrintableArea(document, pageLength);
        DocumentSafeMargins margins = document.DesignMetadata.SafeMargins;
        return new(
            printable.X + margins.Left,
            printable.Y + margins.Top,
            new Micrometre(Math.Max(0, printable.Width.Value - margins.Left.Value - margins.Right.Value)),
            new Micrometre(Math.Max(0, printable.Height.Value - margins.Top.Value - margins.Bottom.Value)));
    }

    private static bool IsOutsideSafeArea(
        LabelDocument document,
        Micrometre pageLength,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds,
        IReadOnlyDictionary<string, int>? previewRotations)
    {
        MicrometreRect safe = GetSafeArea(document, pageLength);
        foreach (DocumentElement element in document.Elements.Where(document.IsEffectivelyVisible))
        {
            MicrometreRect bounds = previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview)
                ? preview
                : element.Bounds;
            int rotation = previewRotations is not null && previewRotations.TryGetValue(element.Id, out int previewRotation)
                ? previewRotation
                : element.RotationMillidegrees;
            MicrometreRect visual = ElementGeometry.RoundBounds(ElementGeometry.GetVisualBounds(bounds, rotation));
            if (visual.X < safe.X || visual.Y < safe.Y || visual.Right > safe.Right || visual.Bottom > safe.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    private static void DrawSnapIndicators(SKCanvas canvas, CanvasTransform view, IReadOnlyList<SnapIndicator>? indicators)
    {
        if (indicators is null || indicators.Count == 0) return;
        using SKPaint paint = new() { Color = new SKColor(0xD0C63F78), StrokeWidth = 1, IsAntialias = true };
        foreach (SnapIndicator indicator in indicators)
        {
            MicrometrePoint start = indicator.Axis == SnapAxis.X
                ? new(indicator.Position, indicator.SpanStart)
                : new(indicator.SpanStart, indicator.Position);
            MicrometrePoint end = indicator.Axis == SnapAxis.X
                ? new(indicator.Position, indicator.SpanEnd)
                : new(indicator.SpanEnd, indicator.Position);
            (double x1, double y1) = view.DocumentToCanvas(start);
            (double x2, double y2) = view.DocumentToCanvas(end);
            canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
        }
    }

    private static void DrawCutHandle(
        SKCanvas canvas,
        LabelDocument document,
        CanvasTransform view,
        Micrometre pageLength,
        bool active)
    {
        if (document.MediaKind != DocumentMediaKind.Continuous) return;
        (double x1, double y1) = view.DocumentToCanvas(Micrometre.Zero, pageLength);
        (double x2, double y2) = view.DocumentToCanvas(document.PageDimensions.Width, pageLength);
        using SKPaint line = new()
        {
            Color = active ? new SKColor(0xFFC63F78) : new SKColor(0xFF6F6870),
            StrokeWidth = active ? 3 : 2,
            IsAntialias = true,
        };
        canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, line);
        float midX = (float)((x1 + x2) / 2);
        float midY = (float)((y1 + y2) / 2);
        canvas.DrawCircle(midX, midY, active ? 6 : 5, line);
        using SKPaint text = new() { Color = line.Color, IsAntialias = true };
        using SKFont font = new(SKTypeface.Default, 11);
        canvas.DrawText($"Cut {pageLength.ToMillimetres():0.0} mm", midX + 8, midY - 6, font, text);
    }

    private static void DrawElements(
        SKCanvas canvas,
        LabelDocument doc,
        double dipsPerMm,
        HashSet<string> selectedElementIds,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds,
        string? editingElementId,
        int viewRotationDegrees,
        IReadOnlyDictionary<string, int>? previewRotations)
    {
        foreach (DocumentElement element in doc.Elements)
        {
            if (element.Id == editingElementId) continue;
            if (!doc.IsEffectivelyVisible(element)) continue;
            bool isSelected = selectedElementIds.Contains(element.Id);

            if (previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview))
            {
                DrawElementAt(canvas, element, dipsPerMm, preview, isSelected, viewRotationDegrees,
                    previewRotations is not null && previewRotations.TryGetValue(element.Id, out int rotation)
                        ? rotation
                        : element.RotationMillidegrees);
            }
            else
            {
                DrawElementAt(canvas, element, dipsPerMm, element.Bounds, isSelected, viewRotationDegrees,
                    previewRotations is not null && previewRotations.TryGetValue(element.Id, out int rotation)
                        ? rotation
                        : element.RotationMillidegrees);
            }
        }
    }

    private static void DrawElementAt(
        SKCanvas canvas,
        DocumentElement element,
        double dipsPerMm,
        MicrometreRect bounds,
        bool isSelected,
        int viewRotationDegrees,
        int rotationMillidegrees)
    {
        int elementState = canvas.Save();
        if (rotationMillidegrees != 0)
        {
            float centerX = (float)DocToCanvas(new Micrometre(bounds.X.Value + bounds.Width.Value / 2), dipsPerMm);
            float centerY = (float)DocToCanvas(new Micrometre(bounds.Y.Value + bounds.Height.Value / 2), dipsPerMm);
            canvas.RotateDegrees(rotationMillidegrees / 1000f, centerX, centerY);
        }

        float x = (float)DocToCanvas(bounds.X, dipsPerMm);
        float y = (float)DocToCanvas(bounds.Y, dipsPerMm);
        float w = (float)DocToCanvas(bounds.Width, dipsPerMm);
        float h = (float)DocToCanvas(bounds.Height, dipsPerMm);

        SKColor inkColor = element.Ink == InkChannel.Red ? new(0xFFCC0000) : ElementFillColor;

        switch (element)
        {
            case RectangleElement rect:
                if (rect.Fill)
                {
                    using SKPaint fill = new() { Color = inkColor, IsAntialias = true };
                    canvas.DrawRect(x, y, w, h, fill);
                }
                else
                {
                    int sw = Math.Max(1, (int)DocToCanvas(rect.StrokeWidth, dipsPerMm));
                    using SKPaint stroke = new() { Color = inkColor, IsStroke = true, StrokeWidth = sw, IsAntialias = true };
                    canvas.DrawRect(x, y, w, h, stroke);
                }
                break;

            case LineElement line:
                int thickness = Math.Max(1, (int)DocToCanvas(line.Thickness, dipsPerMm));
                using (SKPaint linePaint = new() { Color = inkColor, StrokeWidth = thickness, IsStroke = true, IsAntialias = true })
                {
                    int offsetX = bounds.X.Value - line.Bounds.X.Value;
                    int offsetY = bounds.Y.Value - line.Bounds.Y.Value;
                    float sx = (float)DocToCanvas(new Micrometre(line.Start.X.Value + offsetX), dipsPerMm);
                    float sy = (float)DocToCanvas(new Micrometre(line.Start.Y.Value + offsetY), dipsPerMm);
                    float ex = (float)DocToCanvas(new Micrometre(line.End.X.Value + offsetX), dipsPerMm);
                    float ey = (float)DocToCanvas(new Micrometre(line.End.Y.Value + offsetY), dipsPerMm);
                    canvas.DrawLine(sx, sy, ex, ey, linePaint);
                }
                break;

            case ImageElement:
                using (SKPaint imgPaint = new() { Color = new(0xFF888888), IsAntialias = true })
                {
                    canvas.DrawRect(x, y, w, h, imgPaint);
                }
                using (SKPaint imgBorder = new() { Color = ElementStrokeColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true })
                {
                    canvas.DrawRect(x, y, w, h, imgBorder);
                }
                break;

            case TextElement text:
                int textState = canvas.Save();
                canvas.ClipRect(new SKRect(x, y, x + w, y + h));
                float zoom = (float)(dipsPerMm / CanvasTransform.BaseDipsPerMm);
                float fontSize = Math.Max(0.1f, text.FontSizePoints * (96f / 72f) * zoom);
                TextLayoutResult layout = TextLayoutEngine.Layout(
                    text.Text,
                    text.FontFamily,
                    fontSize,
                    (96f / 72f) * zoom,
                    w,
                    h,
                    text.Wrapping,
                    text.Overflow,
                    text.HorizontalAlignment,
                    text.VerticalAlignment);
                using (SKTypeface typeface = SKTypeface.FromFamilyName(text.FontFamily ?? "Segoe UI")
                    ?? SKTypeface.Default
                    ?? SKTypeface.FromFamilyName(null))
                using (SKFont font = new(typeface, layout.EffectiveFontSizePixels))
                using (SKPaint textPaint = new() { Color = inkColor, IsAntialias = true })
                {
                    foreach (TextLayoutLine line in layout.Lines)
                    {
                        canvas.DrawText(line.Text, x + line.X, y + line.Baseline, font, textPaint);
                    }
                }
                canvas.RestoreToCount(textState);
                break;
        }

        canvas.RestoreToCount(elementState);
    }

    private static void DrawSelectionAtScreen(
        SKCanvas canvas,
        LabelDocument doc,
        CanvasTransform view,
        HashSet<string> selectedElementIds,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds,
        IReadOnlyDictionary<string, int>? previewRotations)
    {
        SelectionFrameGeometry? frame = SelectionFrameGeometry.Create(
            doc, selectedElementIds, view, previewBounds, previewRotations);
        if (frame is null) return;

        using SKPaint linePaint = new() { Color = SelectionColor, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
        using SKPaint handlePaint = new() { Color = HandleColor, IsAntialias = true };
        using SKPaint borderPaint = new() { Color = HandleBorderColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true };

        using SKPath outline = new();
        outline.MoveTo((float)frame.Outline[0].X, (float)frame.Outline[0].Y);
        for (int i = 1; i < frame.Outline.Count; i++)
        {
            outline.LineTo((float)frame.Outline[i].X, (float)frame.Outline[i].Y);
        }
        outline.Close();
        canvas.DrawPath(outline, linePaint);

        const float handleSize = 8;
        foreach (Point point in frame.Handles.Values)
        {
            SKRect rect = new(
                (float)point.X - handleSize / 2,
                (float)point.Y - handleSize / 2,
                (float)point.X + handleSize / 2,
                (float)point.Y + handleSize / 2);
            canvas.DrawRect(rect, handlePaint);
            canvas.DrawRect(rect, borderPaint);
        }

        if (selectedElementIds.Count == 1 &&
            doc.Elements.FirstOrDefault(element => element.Id == selectedElementIds.Single()) is TextElement)
        {
            canvas.DrawLine(
                (float)frame.RotationStem.X,
                (float)frame.RotationStem.Y,
                (float)frame.RotationHandle.X,
                (float)frame.RotationHandle.Y,
                linePaint);
            canvas.DrawCircle(
                (float)frame.RotationHandle.X,
                (float)frame.RotationHandle.Y,
                (float)CanvasHandleGeometry.RotationHandleRadius,
                handlePaint);
            canvas.DrawCircle(
                (float)frame.RotationHandle.X,
                (float)frame.RotationHandle.Y,
                (float)CanvasHandleGeometry.RotationHandleRadius,
                borderPaint);
        }
    }

    private static void DrawHover(
        SKCanvas canvas,
        LabelDocument doc,
        double dipsPerMm,
        string? hoverId,
        HashSet<string> selectedElementIds)
    {
        if (hoverId is null || selectedElementIds.Contains(hoverId)) return;

        DocumentElement? element = doc.Elements.FirstOrDefault(e => e.Id == hoverId);
        if (element is null || !doc.IsEffectivelyVisible(element)) return;

        string? groupId = doc.FindGroupIdForMember(hoverId);
        MicrometreRect bounds = groupId is not null
            ? GroupGeometry.GetGroupBounds(doc, groupId)
            : element.Bounds;

        int state = canvas.Save();
        if (groupId is null && element.RotationMillidegrees != 0)
        {
            float centerX = (float)DocToCanvas(new Micrometre(bounds.X.Value + bounds.Width.Value / 2), dipsPerMm);
            float centerY = (float)DocToCanvas(new Micrometre(bounds.Y.Value + bounds.Height.Value / 2), dipsPerMm);
            canvas.RotateDegrees(element.RotationMillidegrees / 1000f, centerX, centerY);
        }

        float x = (float)DocToCanvas(bounds.X, dipsPerMm);
        float y = (float)DocToCanvas(bounds.Y, dipsPerMm);
        float w = (float)DocToCanvas(bounds.Width, dipsPerMm);
        float h = (float)DocToCanvas(bounds.Height, dipsPerMm);

        using SKPaint paint = new() { Color = HoverColor, IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawRect(x - 2, y - 2, w + 4, h + 4, paint);
        canvas.RestoreToCount(state);
    }

    private static void DrawCreationPreview(SKCanvas canvas, double dipsPerMm, MicrometreRect rect)
    {
        float x = (float)DocToCanvas(rect.X, dipsPerMm);
        float y = (float)DocToCanvas(rect.Y, dipsPerMm);
        float w = (float)DocToCanvas(rect.Width, dipsPerMm);
        float h = (float)DocToCanvas(rect.Height, dipsPerMm);

        using SKPaint paint = new() { Color = PreviewColor, IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, paint);
    }
}
