using System.Globalization;
using System.Text;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;
using SkiaSharp;

namespace LabelStudio.Rendering;

public sealed record TextLayoutLine(string Text, float X, float Baseline, float Width);

public sealed record TextLayoutResult(
    float EffectiveFontSizePixels,
    IReadOnlyList<TextLayoutLine> Lines,
    float ContentWidth,
    float ContentHeight);

public static class TextLayoutEngine
{
    public static TextLayoutResult Layout(
        string text,
        string? fontFamily,
        float nominalFontSizePixels,
        float minimumFontSizePixels,
        float frameWidth,
        float frameHeight,
        TextWrappingMode wrapping,
        TextOverflowMode overflow,
        TextHorizontalAlignment horizontalAlignment,
        TextVerticalAlignment verticalAlignment)
    {
        nominalFontSizePixels = Math.Max(minimumFontSizePixels, nominalFontSizePixels);
        frameWidth = Math.Max(0, frameWidth);
        frameHeight = Math.Max(0, frameHeight);

        TextLayoutResult result = LayoutAtSize(
            text, fontFamily, nominalFontSizePixels, frameWidth, frameHeight,
            wrapping, horizontalAlignment, verticalAlignment);

        if (overflow != TextOverflowMode.ShrinkToFit || Fits(result, frameWidth, frameHeight))
        {
            return result;
        }

        float low = Math.Max(0.1f, minimumFontSizePixels);
        float high = nominalFontSizePixels;
        TextLayoutResult smallest = LayoutAtSize(
            text, fontFamily, low, frameWidth, frameHeight,
            wrapping, horizontalAlignment, verticalAlignment);
        if (!Fits(smallest, frameWidth, frameHeight)) return smallest;

        for (int i = 0; i < 14; i++)
        {
            float candidate = (low + high) / 2;
            TextLayoutResult measured = LayoutAtSize(
                text, fontFamily, candidate, frameWidth, frameHeight,
                wrapping, horizontalAlignment, verticalAlignment);
            if (Fits(measured, frameWidth, frameHeight))
            {
                low = candidate;
                smallest = measured;
            }
            else
            {
                high = candidate;
            }
        }

        return smallest;
    }

    public static TextElement ResolveAutomaticFrame(TextElement element)
    {
        if (element.FrameSizing == TextFrameSizingMode.Fixed) return element;

        const float dpi = 96;
        float pixelsPerMicrometre = dpi / 25_400f;
        float fontSize = Math.Max(1, element.FontSizePoints * dpi / 72f);
        float width = element.Bounds.Width.Value * pixelsPerMicrometre;
        TextWrappingMode wrapping = element.FrameSizing == TextFrameSizingMode.AutoSize
            ? TextWrappingMode.NoWrap
            : element.Wrapping;
        float availableWidth = element.FrameSizing == TextFrameSizingMode.AutoSize
            ? 1_000_000
            : width;

        TextLayoutResult layout = Layout(
            element.Text,
            element.FontFamily,
            fontSize,
            dpi / 72f,
            availableWidth,
            1_000_000,
            wrapping,
            element.Overflow,
            TextHorizontalAlignment.Left,
            TextVerticalAlignment.Top);

        int resolvedWidth = element.FrameSizing == TextFrameSizingMode.AutoSize
            ? Math.Max(100, (int)Math.Ceiling(layout.ContentWidth / pixelsPerMicrometre))
            : element.Bounds.Width.Value;
        int resolvedHeight = Math.Max(100, (int)Math.Ceiling(layout.ContentHeight / pixelsPerMicrometre));

        return element with
        {
            Bounds = new MicrometreRect(
                element.Bounds.X,
                element.Bounds.Y,
                new Micrometre(resolvedWidth),
                new Micrometre(resolvedHeight)),
        };
    }

    private static TextLayoutResult LayoutAtSize(
        string text,
        string? fontFamily,
        float fontSize,
        float frameWidth,
        float frameHeight,
        TextWrappingMode wrapping,
        TextHorizontalAlignment horizontalAlignment,
        TextVerticalAlignment verticalAlignment)
    {
        using SKTypeface typeface = SKTypeface.FromFamilyName(fontFamily ?? "Segoe UI")
            ?? SKTypeface.Default
            ?? SKTypeface.FromFamilyName(null);
        using SKFont font = new(typeface, fontSize);

        List<string> lines = BuildLines(text, wrapping, frameWidth, font);
        SKFontMetrics metrics = font.Metrics;
        float glyphHeight = Math.Max(1, metrics.Descent - metrics.Ascent);
        float lineHeight = Math.Max(glyphHeight, font.Spacing);
        float contentHeight = glyphHeight + Math.Max(0, lines.Count - 1) * lineHeight;
        float verticalOffset = verticalAlignment switch
        {
            TextVerticalAlignment.Middle => Math.Max(0, (frameHeight - contentHeight) / 2),
            TextVerticalAlignment.Bottom => Math.Max(0, frameHeight - contentHeight),
            _ => 0,
        };

        List<TextLayoutLine> positioned = [];
        float contentWidth = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = font.MeasureText(lines[i]);
            contentWidth = Math.Max(contentWidth, lineWidth);
            float x = horizontalAlignment switch
            {
                TextHorizontalAlignment.Center => Math.Max(0, (frameWidth - lineWidth) / 2),
                TextHorizontalAlignment.Right => Math.Max(0, frameWidth - lineWidth),
                _ => 0,
            };
            float baseline = verticalOffset - metrics.Ascent + i * lineHeight;
            positioned.Add(new TextLayoutLine(lines[i], x, baseline, lineWidth));
        }

        return new TextLayoutResult(fontSize, positioned, contentWidth, contentHeight);
    }

    private static List<string> BuildLines(
        string text,
        TextWrappingMode wrapping,
        float frameWidth,
        SKFont font)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        List<string> lines = [];
        foreach (string paragraph in normalized.Split('\n'))
        {
            if (wrapping == TextWrappingMode.NoWrap || frameWidth <= 0)
            {
                lines.Add(paragraph);
                continue;
            }

            WrapParagraph(paragraph, frameWidth, font, lines);
        }

        if (lines.Count == 0) lines.Add(string.Empty);
        return lines;
    }

    private static void WrapParagraph(string paragraph, float frameWidth, SKFont font, List<string> output)
    {
        if (paragraph.Length == 0)
        {
            output.Add(string.Empty);
            return;
        }

        List<string> graphemes = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(paragraph);
        while (enumerator.MoveNext()) graphemes.Add(enumerator.GetTextElement());

        int start = 0;
        while (start < graphemes.Count)
        {
            var builder = new StringBuilder();
            int lastBreak = -1;
            int cursor = start;
            for (; cursor < graphemes.Count; cursor++)
            {
                builder.Append(graphemes[cursor]);
                if (string.IsNullOrWhiteSpace(graphemes[cursor])) lastBreak = cursor;
                if (font.MeasureText(builder.ToString()) > frameWidth) break;
            }

            if (cursor == graphemes.Count)
            {
                output.Add(builder.ToString().TrimEnd());
                break;
            }

            int end = lastBreak >= start ? lastBreak : Math.Max(start, cursor - 1);
            if (end == start && font.MeasureText(graphemes[start]) <= frameWidth && cursor > start)
            {
                end = cursor - 1;
            }

            string line = string.Concat(graphemes.GetRange(start, end - start + 1)).TrimEnd();
            output.Add(line);
            start = end + 1;
            while (start < graphemes.Count && string.IsNullOrWhiteSpace(graphemes[start])) start++;
        }
    }

    private static bool Fits(TextLayoutResult result, float width, float height) =>
        result.ContentWidth <= width + 0.01f && result.ContentHeight <= height + 0.01f;
}
