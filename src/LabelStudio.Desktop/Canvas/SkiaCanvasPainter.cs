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

        DrawLabelArea(canvas, doc, view);
        DrawPrintableArea(canvas, doc, view);
        DrawElements(canvas, doc, view, selectedElementIds, previewBounds);
        DrawSelection(canvas, doc, view, editor.Selection, selectedElementIds, previewBounds);
        DrawHover(canvas, doc, view, hoverElementId);

        if (creationPreview is not null)
        {
            DrawCreationPreview(canvas, view, creationPreview.Value);
        }

        canvas.RestoreToCount(savedState);
    }

    private static void DrawLabelArea(SKCanvas canvas, LabelDocument doc, CanvasTransform view)
    {
        double x = view.DocumentToCanvasX(Micrometre.Zero);
        double y = view.DocumentToCanvasY(Micrometre.Zero);
        double w = view.DocumentToCanvasLength(doc.PageDimensions.Width);
        double h = view.DocumentToCanvasLength(doc.PageDimensions.Height);

        if (h < 1) h = view.DocumentToCanvasLength(new Micrometre(200_000));

        using SKPaint paint = new() { Color = LabelColor, IsAntialias = true };
        canvas.DrawRect((float)x, (float)y, (float)w, (float)h, paint);

        using SKPaint border = new() { Color = LabelBorderColor, IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect((float)x, (float)y, (float)w, (float)h, border);
    }

    private static void DrawPrintableArea(SKCanvas canvas, LabelDocument doc, CanvasTransform view)
    {
        MicrometreRect printable = doc.MediaGeometry.PrintableArea;
        if (printable.Width <= Micrometre.Zero) return;

        double x = view.DocumentToCanvasX(printable.X);
        double y = view.DocumentToCanvasY(printable.Y);
        double w = view.DocumentToCanvasLength(printable.Width);
        double h = view.DocumentToCanvasLength(printable.Height);
        if (h < 1) h = view.DocumentToCanvasLength(doc.PageDimensions.Height);

        using SKPaint fill = new() { Color = PrintableColor, IsAntialias = true };
        canvas.DrawRect((float)x, (float)y, (float)w, (float)h, fill);

        using SKPaint border = new() { Color = PrintableBorderColor, IsStroke = true, StrokeWidth = 0.5f, IsAntialias = true };
        canvas.DrawRect((float)x, (float)y, (float)w, (float)h, border);
    }

    private static void DrawElements(SKCanvas canvas, LabelDocument doc, CanvasTransform view, HashSet<string> selectedElementIds, IReadOnlyDictionary<string, MicrometreRect>? previewBounds)
    {
        foreach (DocumentElement element in doc.Elements)
        {
            if (!doc.IsEffectivelyVisible(element)) continue;
            bool isSelected = selectedElementIds.Contains(element.Id);

            if (previewBounds is not null && previewBounds.TryGetValue(element.Id, out MicrometreRect preview))
            {
                DrawElementAt(canvas, element, view, preview, isSelected);
            }
            else
            {
                DrawElementAt(canvas, element, view, element.Bounds, isSelected);
            }
        }
    }

    private static void DrawElement(SKCanvas canvas, DocumentElement element, CanvasTransform view, bool isSelected)
        => DrawElementAt(canvas, element, view, element.Bounds, isSelected);

    private static void DrawElementAt(SKCanvas canvas, DocumentElement element, CanvasTransform view, MicrometreRect bounds, bool isSelected)
    {
        float x = (float)view.DocumentToCanvasX(bounds.X);
        float y = (float)view.DocumentToCanvasY(bounds.Y);
        float w = (float)view.DocumentToCanvasLength(bounds.Width);
        float h = (float)view.DocumentToCanvasLength(bounds.Height);

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
                    int sw = Math.Max(1, (int)view.DocumentToCanvasLength(rect.StrokeWidth));
                    using SKPaint stroke = new() { Color = inkColor, IsStroke = true, StrokeWidth = sw, IsAntialias = true };
                    canvas.DrawRect(x, y, w, h, stroke);
                }
                break;

            case LineElement line:
                int thickness = Math.Max(1, (int)view.DocumentToCanvasLength(line.Thickness));
                using (SKPaint linePaint = new() { Color = inkColor, StrokeWidth = thickness, IsStroke = true, IsAntialias = true })
                {
                    int offsetX = bounds.X.Value - line.Bounds.X.Value;
                    int offsetY = bounds.Y.Value - line.Bounds.Y.Value;
                    float sx = (float)view.DocumentToCanvasX(new Micrometre(line.Start.X.Value + offsetX));
                    float sy = (float)view.DocumentToCanvasY(new Micrometre(line.Start.Y.Value + offsetY));
                    float ex = (float)view.DocumentToCanvasX(new Micrometre(line.End.X.Value + offsetX));
                    float ey = (float)view.DocumentToCanvasY(new Micrometre(line.End.Y.Value + offsetY));
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
                float fontSize = Math.Max(8, (float)view.DocumentToCanvasLength(new Micrometre(text.Bounds.Height.Value)));
                using (SKPaint textBg = new() { Color = new(0x22444444), IsAntialias = true })
                {
                    canvas.DrawRect(x, y, w, h, textBg);
                }
                using (SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI"))
                using (SKFont font = new(typeface, fontSize * 0.7f))
                using (SKPaint textPaint = new() { Color = inkColor, IsAntialias = true })
                {
                    float textY = y + fontSize * 0.6f;
                    canvas.DrawText(text.Text, x + 4, textY, font, textPaint);
                }
                break;
        }
    }

    private static void DrawSelection(SKCanvas canvas, LabelDocument doc, CanvasTransform view, SelectionModel selection, HashSet<string> selectedElementIds, IReadOnlyDictionary<string, MicrometreRect>? previewBounds)
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
            float x = (float)view.DocumentToCanvasX(bounds.X);
            float y = (float)view.DocumentToCanvasY(bounds.Y);
            float w = (float)view.DocumentToCanvasLength(bounds.Width);
            float h = (float)view.DocumentToCanvasLength(bounds.Height);

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

        float cx = (float)view.DocumentToCanvasX(combinedBounds.X);
        float cy = (float)view.DocumentToCanvasY(combinedBounds.Y);
        float cw = (float)view.DocumentToCanvasLength(combinedBounds.Width);
        float ch = (float)view.DocumentToCanvasLength(combinedBounds.Height);

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

    private static void DrawHover(SKCanvas canvas, LabelDocument doc, CanvasTransform view, string? hoverId)
    {
        if (hoverId is null) return;

        DocumentElement? element = doc.Elements.FirstOrDefault(e => e.Id == hoverId);
        if (element is null || !doc.IsEffectivelyVisible(element)) return;

        string? groupId = doc.FindGroupIdForMember(hoverId);
        MicrometreRect bounds = groupId is not null
            ? GroupGeometry.GetGroupBounds(doc, groupId)
            : element.Bounds;

        float x = (float)view.DocumentToCanvasX(bounds.X);
        float y = (float)view.DocumentToCanvasY(bounds.Y);
        float w = (float)view.DocumentToCanvasLength(bounds.Width);
        float h = (float)view.DocumentToCanvasLength(bounds.Height);

        using SKPaint paint = new() { Color = HoverColor, IsAntialias = true };
        canvas.DrawRect(x - 2, y - 2, w + 4, h + 4, paint);
    }

    private static void DrawCreationPreview(SKCanvas canvas, CanvasTransform view, MicrometreRect rect)
    {
        float x = (float)view.DocumentToCanvasX(rect.X);
        float y = (float)view.DocumentToCanvasY(rect.Y);
        float w = (float)view.DocumentToCanvasLength(rect.Width);
        float h = (float)view.DocumentToCanvasLength(rect.Height);

        using SKPaint paint = new() { Color = PreviewColor, IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, paint);
    }
}
