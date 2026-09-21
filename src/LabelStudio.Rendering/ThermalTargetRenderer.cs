using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;

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

        return new RenderedPlanes(black, red);
    }

    private static void RenderElement(MonochromeRaster plane, PreparedElement element, RenderTarget target)
    {
        MicrometreRect bounds = element.Bounds;
        int x = ToDot(bounds.X, target.DpiX) + target.HeadLeftBlankDots;
        int y = ToDot(bounds.Y, target.DpiY);
        int w = ToDot(bounds.Width, target.DpiX);
        int h = ToDot(bounds.Height, target.DpiY);

        if (w <= 0 && h <= 0) return;

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
                int fontSizeDots = Math.Max(1, ToDot(
                    new Micrometre(element.Bounds.Height.Value), target.DpiY));
                DrawSimpleText(plane, element.Content.TextContent ?? "", x, y, w, h, fontSizeDots);
                break;
        }
    }

    private static int ToDot(Micrometre um, int dpi) =>
        PhysicalUnits.MicrometresToDots(um.Value, dpi);

    private static void DrawSimpleText(
        MonochromeRaster plane, string text, int x, int y, int w, int h, int fontDots)
    {
        if (string.IsNullOrEmpty(text)) return;

        int charWidth = Math.Max(3, fontDots / 2);
        int charHeight = fontDots;
        int spacing = Math.Max(1, charWidth / 4);
        int totalWidth = text.Length * (charWidth + spacing);

        if (totalWidth > w)
        {
            int available = w - charWidth;
            int charsThatFit = Math.Max(1, available / (charWidth + spacing));
            text = text[..Math.Min(text.Length, charsThatFit)];
        }

        int startX = x + Math.Max(0, (w - text.Length * (charWidth + spacing)) / 2);
        int startY = y + Math.Max(0, (h - charHeight) / 2);

        for (int i = 0; i < text.Length; i++)
        {
            int cx = startX + i * (charWidth + spacing);
            DrawSimpleChar(plane, text[i], cx, startY, charWidth, charHeight);
        }
    }

    private static void DrawSimpleChar(MonochromeRaster plane, char c, int x, int y, int w, int h)
    {
        int thickness = Math.Max(1, w / 6);

        if (char.IsLetterOrDigit(c) || c == '-' || c == ' ')
        {
            if (c != ' ')
            {
                plane.DrawRectangle(x, y, w, h, thickness);
            }
        }
        else
        {
            plane.DrawRectangle(x, y, w, h, thickness);
        }
    }
}