using LabelStudio.Desktop.Canvas;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Editor;
using SkiaSharp;
using System.Collections.Generic;

namespace LabelStudio.Desktop.Tests;

public sealed class TextRenderingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void TextRemainsVisibleInEveryViewRotation(int rotation)
    {
        PhysicalSize dimensions = new(new(62_000), Micrometre.Zero);
        MediaSnapshot snapshot = new(
            "test",
            dimensions,
            new MicrometreRect(new(1500), Micrometre.Zero, new(58_900), Micrometre.Zero));
        MicrometreRect bounds = rotation is 90 or 270
            ? new(new(10_000), new(10_000), new(10_000), new(30_000))
            : new(new(10_000), new(10_000), new(30_000), new(10_000));
        LabelDocument document = LabelDocument.Create(
            dimensions,
            "test",
            snapshot,
            [new TextElement("text", bounds, InkChannel.Black, "ABCD", 24, null)]);
        EditorState editor = new(document);
        editor.ViewTransform.SetContentSize(new(62_000), new(200_000));
        editor.ViewTransform.ViewRotationDegrees = rotation;
        editor.ViewTransform.OffsetX = 100;
        editor.ViewTransform.OffsetY = 100;

        using SKBitmap bitmap = new(new SKImageInfo(800, 800));
        using SKCanvas canvas = new(bitmap);
        new SkiaCanvasPainter().Paint(canvas, 800, 800, 1, editor, null, null);

        (double left, double top, double right, double bottom) = GetScreenBounds(editor.ViewTransform, bounds);
        bool foundDarkPixel = false;
        for (int y = Math.Max(0, (int)Math.Floor(top)); y <= Math.Min(799, (int)Math.Ceiling(bottom)); y++)
        {
            for (int x = Math.Max(0, (int)Math.Floor(left)); x <= Math.Min(799, (int)Math.Ceiling(right)); x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100)
                {
                    foundDarkPixel = true;
                    break;
                }
            }

            if (foundDarkPixel) break;
        }

        Assert.True(foundDarkPixel, $"No rendered text pixels found at view rotation {rotation}.");
    }

    [Fact]
    public void TextUsesPreviewBoundsDuringResize()
    {
        PhysicalSize dimensions = new(new(62_000), Micrometre.Zero);
        MediaSnapshot snapshot = new(
            "test",
            dimensions,
            new MicrometreRect(new(1500), Micrometre.Zero, new(58_900), Micrometre.Zero));
        MicrometreRect original = new(new(10_000), new(10_000), new(30_000), new(10_000));
        MicrometreRect preview = new(new(30_000), new(10_000), new(20_000), new(10_000));
        LabelDocument document = LabelDocument.Create(
            dimensions,
            "test",
            snapshot,
            [new TextElement("text", original, InkChannel.Black, "ABCD", 24, null)]);
        EditorState editor = new(document);
        editor.ViewTransform.SetContentSize(new(62_000), new(200_000));
        editor.ViewTransform.OffsetX = 100;
        editor.ViewTransform.OffsetY = 100;

        using SKBitmap bitmap = new(new SKImageInfo(800, 800));
        using SKCanvas canvas = new(bitmap);
        new SkiaCanvasPainter().Paint(
            canvas,
            800,
            800,
            1,
            editor,
            null,
            null,
            new Dictionary<string, MicrometreRect> { ["text"] = preview });

        Assert.True(HasDarkPixel(bitmap, 190, 125, 270, 160));
        Assert.False(HasDarkPixel(bitmap, 130, 125, 180, 160));
    }

    [Fact]
    public void HoveringSelectedRotatedText_DoesNotAddOverlayFill()
    {
        PhysicalSize dimensions = new(new(17_000), new(53_900));
        MediaSnapshot snapshot = new(
            "test",
            dimensions,
            new MicrometreRect(new(1_500), new(3_000), new(14_000), new(47_900)));
        TextElement text = new(
            "text",
            new MicrometreRect(new(-1_500), new(20_000), new(20_000), new(8_000)),
            InkChannel.Black,
            string.Empty,
            12,
            null)
        {
            RotationMillidegrees = 270_000,
        };
        EditorState editor = new(LabelDocument.Create(dimensions, "test", snapshot, [text]));
        editor.Selection.SelectElement(text.Id);
        editor.ViewTransform.SetContentSize(dimensions.Width, dimensions.Height);
        editor.ViewTransform.ViewRotationDegrees = 90;
        editor.ViewTransform.OffsetX = 100;
        editor.ViewTransform.OffsetY = 100;

        using SKBitmap withoutHover = new(new SKImageInfo(800, 800));
        using SKCanvas withoutHoverCanvas = new(withoutHover);
        new SkiaCanvasPainter().Paint(withoutHoverCanvas, 800, 800, 1, editor, null, null);

        using SKBitmap withHover = new(new SKImageInfo(800, 800));
        using SKCanvas withHoverCanvas = new(withHover);
        new SkiaCanvasPainter().Paint(withHoverCanvas, 800, 800, 1, editor, text.Id, null);

        Assert.True(withoutHover.Bytes.SequenceEqual(withHover.Bytes));
    }

    private static bool HasDarkPixel(SKBitmap bitmap, int left, int top, int right, int bottom)
    {
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100) return true;
            }
        }

        return false;
    }

    private static (double Left, double Top, double Right, double Bottom) GetScreenBounds(
        CanvasTransform view,
        MicrometreRect bounds)
    {
        (double x1, double y1) = view.DocumentToCanvas(bounds.X, bounds.Y);
        (double x2, double y2) = view.DocumentToCanvas(bounds.Right, bounds.Y);
        (double x3, double y3) = view.DocumentToCanvas(bounds.Right, bounds.Bottom);
        (double x4, double y4) = view.DocumentToCanvas(bounds.X, bounds.Bottom);
        return (
            Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
            Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
            Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
            Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }
}
