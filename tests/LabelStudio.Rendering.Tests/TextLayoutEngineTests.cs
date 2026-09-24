using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using SkiaSharp;

namespace LabelStudio.Rendering.Tests;

public class TextLayoutEngineTests
{
    [Fact]
    public void WrapAndNoWrap_AreIndependentFromOverflowMode()
    {
        TextLayoutResult wrapped = TextLayoutEngine.Layout(
            "alpha beta gamma", null, 20, 1, 70, 200,
            TextWrappingMode.Wrap, TextOverflowMode.Clip,
            TextHorizontalAlignment.Left, TextVerticalAlignment.Top);
        TextLayoutResult unwrapped = TextLayoutEngine.Layout(
            "alpha beta gamma", null, 20, 1, 70, 200,
            TextWrappingMode.NoWrap, TextOverflowMode.Clip,
            TextHorizontalAlignment.Left, TextVerticalAlignment.Top);

        Assert.True(wrapped.Lines.Count > 1);
        Assert.Single(unwrapped.Lines);
        Assert.Equal(20, wrapped.EffectiveFontSizePixels);
    }

    [Fact]
    public void ShrinkToFit_ReducesFontWithoutChangingFrame()
    {
        TextLayoutResult result = TextLayoutEngine.Layout(
            "A long line of text", null, 30, 2, 80, 30,
            TextWrappingMode.NoWrap, TextOverflowMode.ShrinkToFit,
            TextHorizontalAlignment.Left, TextVerticalAlignment.Top);

        Assert.InRange(result.EffectiveFontSizePixels, 2, 29.99f);
        Assert.True(result.ContentWidth <= 80.01f);
        Assert.True(result.ContentHeight <= 30.01f);
    }

    [Fact]
    public void Alignment_PositionsContentInsideFrame()
    {
        TextLayoutResult topLeft = TextLayoutEngine.Layout(
            "Text", null, 20, 1, 200, 100,
            TextWrappingMode.NoWrap, TextOverflowMode.Clip,
            TextHorizontalAlignment.Left, TextVerticalAlignment.Top);
        TextLayoutResult bottomRight = TextLayoutEngine.Layout(
            "Text", null, 20, 1, 200, 100,
            TextWrappingMode.NoWrap, TextOverflowMode.Clip,
            TextHorizontalAlignment.Right, TextVerticalAlignment.Bottom);

        Assert.Equal(0, topLeft.Lines[0].X);
        Assert.True(bottomRight.Lines[0].X > topLeft.Lines[0].X);
        Assert.True(bottomRight.Lines[0].Baseline > topLeft.Lines[0].Baseline);
    }

    [Fact]
    public void CenterAlignment_CentersVisibleGlyphBoundsInFrame()
    {
        const string value = "Hello";
        const float frameWidth = 400;
        const float fontSize = 72;

        TextLayoutResult result = TextLayoutEngine.Layout(
            value, "Segoe UI", fontSize, 1, frameWidth, 120,
            TextWrappingMode.NoWrap, TextOverflowMode.Clip,
            TextHorizontalAlignment.Center, TextVerticalAlignment.Top);

        using SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using SKFont font = new(typeface, fontSize);
        font.MeasureText(value, out SKRect glyphBounds);

        float visibleCenter = result.Lines[0].X + (glyphBounds.Left + glyphBounds.Right) / 2;
        Assert.Equal(frameWidth / 2, visibleCenter, precision: 3);
    }

    [Fact]
    public void AutomaticFrame_ChangesOnlyConfiguredDimensions()
    {
        TextElement autoHeight = new(
            "t1",
            new MicrometreRect(new(1000), new(2000), new(8000), new(100)),
            InkChannel.Black,
            "one two three four five six",
            20,
            null)
        {
            FrameSizing = TextFrameSizingMode.AutoHeight,
            Wrapping = TextWrappingMode.Wrap,
        };
        TextElement autoSize = autoHeight with { FrameSizing = TextFrameSizingMode.AutoSize };

        TextElement resolvedHeight = TextLayoutEngine.ResolveAutomaticFrame(autoHeight);
        TextElement resolvedSize = TextLayoutEngine.ResolveAutomaticFrame(autoSize);

        Assert.Equal(autoHeight.Bounds.Width, resolvedHeight.Bounds.Width);
        Assert.True(resolvedHeight.Bounds.Height > autoHeight.Bounds.Height);
        Assert.True(resolvedSize.Bounds.Width > Micrometre.Zero);
        Assert.True(resolvedSize.Bounds.Height > Micrometre.Zero);
    }
}
