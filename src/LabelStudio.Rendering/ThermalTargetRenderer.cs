using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;
using SkiaSharp;

namespace LabelStudio.Rendering;

public sealed class ThermalTargetRenderer : ITargetRenderer
{
    public RenderedPlanes Render(PreparedScene scene, RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(target);

        MonochromeRaster black = new(target.PhysicalTargetWidthDots, target.PhysicalTargetHeightDots);
        MonochromeRaster? red = target.SupportsRedPlane
            ? new MonochromeRaster(target.PhysicalTargetWidthDots, target.PhysicalTargetHeightDots)
            : null;

        foreach (PreparedElement element in scene.Elements)
        {
            if (element.Ink == InkChannel.Transparent) continue;

            MonochromeRaster plane = element.Ink switch
            {
                InkChannel.Red => red ?? black,
                _ => black,
            };

            RenderElement(plane, element, target);
        }

        black.ClearOutsideHorizontalRange(target.HeadLeftBlankDots, target.PrintableWidthDots);
        red?.ClearOutsideHorizontalRange(target.HeadLeftBlankDots, target.PrintableWidthDots);

        return new RenderedPlanes(black, red);
    }

    private static void RenderElement(MonochromeRaster plane, PreparedElement element, RenderTarget target)
    {
        MicrometreRect bounds = element.Bounds;
        int x = ToDot(bounds.X - target.DocumentOriginX, target.DpiX) + target.HeadLeftBlankDots;
        int y = ToDot(bounds.Y - target.DocumentOriginY, target.DpiY);
        int w = ToDot(bounds.Width, target.DpiX);
        int h = ToDot(bounds.Height, target.DpiY);

        if (w <= 0 || h <= 0) return;

        w = Math.Max(1, w);
        h = Math.Max(1, h);

        switch (element.Content.ContentType)
        {
            case PreparedContentType.Rectangle:
                if (element.IsFilled)
                {
                    plane.FillRectangle(x, y, w, h);
                }
                else
                {
                    int stroke = Math.Max(1, ToDot(element.StrokeWidth, target.DpiX));
                    plane.DrawRectangle(x, y, w, h, stroke);
                }
                break;

            case PreparedContentType.Line:
                int thickness = Math.Max(1, ToDot(element.LineThickness, target.DpiX));
                bool horizontal = bounds.Width >= bounds.Height;
                if (horizontal)
                {
                    plane.DrawHorizontalLine(x, y + h / 2, w, thickness);
                }
                else
                {
                    plane.DrawVerticalLine(x + w / 2, y, h, thickness);
                }
                break;

            case PreparedContentType.Image:
                plane.FillRectangle(x, y, w, h);
                break;

            case PreparedContentType.Text:
                DrawText(plane, element, target, x, y, w, h);
                break;
        }
    }

    private static int ToDot(Micrometre um, int dpi) =>
        PhysicalUnits.MicrometresToDots(um.Value, dpi);

    private static void DrawText(
        MonochromeRaster plane,
        PreparedElement element,
        RenderTarget target,
        int x,
        int y,
        int width,
        int height)
    {
        string text = element.Content.TextContent ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        int fontSizePoints = element.TextFontSizePoints > 0 ? element.TextFontSizePoints : 12;
        float fontSizeDots = Math.Max(1, fontSizePoints * target.DpiY / 72f);
        TextLayoutResult layout = TextLayoutEngine.Layout(
            text,
            element.TextFontFamily,
            fontSizeDots,
            target.DpiY / 72f,
            width,
            height,
            element.TextWrapping,
            element.TextOverflow,
            element.TextHorizontalAlignment,
            element.TextVerticalAlignment);

        using SKBitmap bitmap = new(new SKImageInfo(
            plane.Width,
            plane.Height,
            SKColorType.Alpha8,
            SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);
        if (element.RotationMillidegrees != 0)
        {
            canvas.RotateDegrees(
                element.RotationMillidegrees / 1000f,
                x + width / 2f,
                y + height / 2f);
        }
        canvas.ClipRect(new SKRect(x, y, x + width, y + height));

        using SKTypeface typeface = SKTypeface.FromFamilyName(element.TextFontFamily ?? "Segoe UI")
            ?? SKTypeface.Default
            ?? SKTypeface.FromFamilyName(null);
        using SKFont font = new(typeface, layout.EffectiveFontSizePixels);
        using SKPaint paint = new() { Color = SKColors.White, IsAntialias = true };

        foreach (TextLayoutLine line in layout.Lines)
        {
            canvas.DrawText(line.Text, x + line.X, y + line.Baseline, font, paint);
        }
        canvas.Flush();

        for (int py = 0; py < plane.Height; py++)
        {
            for (int px = 0; px < plane.Width; px++)
            {
                if (bitmap.GetPixel(px, py).Alpha >= 128)
                {
                    plane.SetPixel(px, py);
                }
            }
        }
    }

}
