using SkiaSharp;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using LabelStudio.Editor.Selection;
using LabelStudio.Editor.Transforms;
using SelectionTarget = LabelStudio.Editor.Selection.SelectionTarget;

namespace LabelStudio.Desktop.Canvas;

public sealed class SkiaCanvasPainter
{
    private static readonly SKColor WorkspaceColor = new(0xFF2D2D30);
    private static readonly SKColor LabelColor = new(0xFFFFFFFF);
    private static readonly SKColor PrintableColor = new(0xFFE8E8E8);
    private static readonly SKColor PrintableBorderColor = new(0xFFB0B0B0);
    private static readonly SKColor LabelBorderColor = new(0xFF888888);
    private static readonly SKColor ElementFillColor = new(0xFF333333);
    private static readonly SKColor ElementStrokeColor = new(0xFF000000);
    private static readonly SKColor SelectionColor = new(0xFF007ACC);
    private static readonly SKColor HandleColor = new(0xFFFFFFFF);
    private static readonly SKColor HandleBorderColor = new(0xFF007ACC);
    private static readonly SKColor HoverColor = new(0x40007ACC);
    private static readonly SKColor PreviewColor = new(0x66007ACC);

    public void Paint(
        SKCanvas canvas,
        int surfaceWidth,
        int surfaceHeight,
        float renderScale,
        EditorState editor,
        string? hoverElementId,
        MicrometreRect? creationPreview,
        IReadOnlyDictionary<string, MicrometreRect>? previewBounds = null)
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
                (float)cos, (float)-sin, (float)view.OffsetX,
                (float)sin, (float)cos, (float)view.OffsetY,
                0, 0, 1);
            canvas.Concat(rotation);
        }
        else
        {
            canvas.Translate((float)view.OffsetX, (float)view.OffsetY);
        }

        double dipsPerMm = view.DipsPerMm;

        DrawLabelArea(canvas, doc, dipsPerMm);
        DrawPrintableArea(canvas, doc, dipsPerMm);
        DrawElements(canvas, doc, dipsPerMm, selectedElementIds, previewBounds);
        DrawSelection(canvas, doc, dipsPerMm, editor.Selection, selectedElementIds, previewBounds);
        DrawHover(canvas, doc, dipsPerMm, hoverElementId);

        if (creationPreview is not null)
        {
            DrawCreationPreview(canvas, dipsPerMm, creationPreview.Value);
        }

        canvas.RestoreToCount(rotationState);
        canvas.RestoreToCount(savedState);
    }

    private static double DocToCanvas(Micrometre value, double dipsPerMm) =>
        value.Value / 1000.0 * dipsPerMm;

    private static void DrawLabelArea(SKCanvas canvas, LabelDocument doc, double dipsPerMm)
    {
        float x = 0f;
        float y = 0f;
        float w = (float)DocToCanvas(doc.PageDimensions.Width, dipsPerMm);
        float h = (float)(doc.PageDimensions.Height > Micrometre.Zero
            ? DocToCanvas(doc.PageDimensions.Height, dipsPerMm)
            : DocToCanvas(new Micrometre(200_000), dipsPerMm));

        using SKPaint paint = new() { Color = LabelColor, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, paint);

        using SKPaint border = new() { Color = LabelBorderColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, border);
    }

    private static void DrawPrintableArea(SKCanvas canvas, LabelDocument doc, double dipsPerMm)
    {
        MicrometreRect printable = doc.MediaGeometry.PrintableArea;
        if (printable.Width <= Micrometre.Zero) return;

        float x = (float)DocToCanvas(printable.X, dipsPerMm);
        float y = (float)DocToCanvas(printable.Y, dipsPerMm);
        float w = (float)DocToCanvas(printable.Width, dipsPerMm);
        float h = (float)(printable.Height > Micrometre.Zero
            ? DocToCanvas(printable.Height, dipsPerMm)
            : DocToCanvas(doc.PageDimensions.Height > Micrometre.Zero ? doc.PageDimensions.Height : new Micrometre(200_000), dipsPerMm));

        using SKPaint fill = new() { Color = PrintableColor, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, fill);

        using SKPaint border = new() { Color = PrintableBorderColor, IsStroke = true, StrokeWidth = 0.5f, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, border);
    }

    private static void DrawElements(SKCanvas canvas, LabelDocument doc, double dipsPerMm, HashSet<string> selectedElementIds, IReadOnlyDictionary<string, MicrometreRect>? previewBounds)
    {
        foreach (DocumentElement element in doc.Elements)
        {
            if (!doc.IsEffectivelyVisible(element)) continue;
            bool isSelected = selectedElementIds.Contains(element.Id);

            if (previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview))
            {
                DrawElementAt(canvas, element, dipsPerMm, preview, isSelected);
            }
            else
            {
                DrawElementAt(canvas, element, dipsPerMm, element.Bounds, isSelected);
            }
        }
    }

    private static void DrawElementAt(SKCanvas canvas, DocumentElement element, double dipsPerMm, MicrometreRect bounds, bool isSelected)
    {
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
                float fontSize = Math.Max(8, (float)DocToCanvas(new Micrometre(text.Bounds.Height.Value), dipsPerMm));
                using (SKPaint textBg = new() { Color = new(0x22444444), IsAntialias = true })
                {
                    canvas.DrawRect(x, y, w, h, textBg);
                }
                using (SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI")
                    ?? SKTypeface.Default
                    ?? SKTypeface.FromFamilyName(null))
                using (SKFont font = new(typeface, fontSize * 0.7f))
                using (SKPaint textPaint = new() { Color = inkColor, IsAntialias = true })
                {
                    float textY = y + fontSize * 0.6f;
                    canvas.DrawText(text.Text, x + 4, textY, font, textPaint);
                }
                break;
        }
    }

    private static void DrawSelection(SKCanvas canvas, LabelDocument doc, double dipsPerMm, SelectionModel selection, HashSet<string> selectedElementIds, IReadOnlyDictionary<string, MicrometreRect>? previewBounds)
    {
        if (!selection.HasSelection) return;

        using SKPaint selPaint = new() { Color = SelectionColor, IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };

        foreach (string id in selectedElementIds)
        {
            DocumentElement? element = doc.Elements.FirstOrDefault(e => e.Id == id);
            if (element is null || !doc.IsEffectivelyVisible(element)) continue;

            MicrometreRect bounds = previewBounds is not null && previewBounds.TryGetValue(id, out MicrometreRect preview)
                ? preview
                : element.Bounds;
            float x = (float)DocToCanvas(bounds.X, dipsPerMm);
            float y = (float)DocToCanvas(bounds.Y, dipsPerMm);
            float w = (float)DocToCanvas(bounds.Width, dipsPerMm);
            float h = (float)DocToCanvas(bounds.Height, dipsPerMm);

            canvas.DrawRect(x - 2, y - 2, w + 4, h + 4, selPaint);
        }

        MicrometreRect combinedBounds;
        if (previewBounds is not null && previewBounds.Count > 0)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (MicrometreRect b in previewBounds.Values)
            {
                minX = Math.Min(minX, b.X.Value);
                minY = Math.Min(minY, b.Y.Value);
                maxX = Math.Max(maxX, b.Right.Value);
                maxY = Math.Max(maxY, b.Bottom.Value);
            }
            combinedBounds = new MicrometreRect(new(minX), new(minY), new(maxX - minX), new(maxY - minY));
        }
        else
        {
            combinedBounds = SelectionBounds.GetCombinedBounds(doc, selectedElementIds);
        }

        if (combinedBounds.Width <= Micrometre.Zero && combinedBounds.Height <= Micrometre.Zero) return;

        float cx = (float)DocToCanvas(combinedBounds.X, dipsPerMm);
        float cy = (float)DocToCanvas(combinedBounds.Y, dipsPerMm);
        float cw = (float)DocToCanvas(combinedBounds.Width, dipsPerMm);
        float ch = (float)DocToCanvas(combinedBounds.Height, dipsPerMm);

        using SKPaint combinedPaint = new()
        {
            Color = SelectionColor,
            IsStroke = true,
            StrokeWidth = 2f,
            IsAntialias = true,
        };
        canvas.DrawRect(cx - 4, cy - 4, cw + 8, ch + 8, combinedPaint);
        DrawHandles(canvas, cx, cy, cw, ch);
    }

    private static void DrawHandles(SKCanvas canvas, float x, float y, float w, float h)
    {
        float hs = 8f;
        float[] xs = [x - hs / 2, x + w / 2 - hs / 2, x + w - hs / 2];
        float[] ys = [y - hs / 2, y + h / 2 - hs / 2, y + h - hs / 2];

        using SKPaint handlePaint = new() { Color = HandleColor, IsAntialias = true };
        using SKPaint borderPaint = new() { Color = HandleBorderColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true };

        foreach (float hy in ys)
        {
            foreach (float hx in xs)
            {
                canvas.DrawRect(hx, hy, hs, hs, handlePaint);
                canvas.DrawRect(hx, hy, hs, hs, borderPaint);
            }
        }
    }

    private static void DrawHover(SKCanvas canvas, LabelDocument doc, double dipsPerMm, string? hoverId)
    {
        if (hoverId is null) return;

        DocumentElement? element = doc.Elements.FirstOrDefault(e => e.Id == hoverId);
        if (element is null || !doc.IsEffectivelyVisible(element)) return;

        string? groupId = doc.FindGroupIdForMember(hoverId);
        MicrometreRect bounds = groupId is not null
            ? GroupGeometry.GetGroupBounds(doc, groupId)
            : element.Bounds;

        float x = (float)DocToCanvas(bounds.X, dipsPerMm);
        float y = (float)DocToCanvas(bounds.Y, dipsPerMm);
        float w = (float)DocToCanvas(bounds.Width, dipsPerMm);
        float h = (float)DocToCanvas(bounds.Height, dipsPerMm);

        using SKPaint paint = new() { Color = HoverColor, IsAntialias = true };
        canvas.DrawRect(x - 2, y - 2, w + 4, h + 4, paint);
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
