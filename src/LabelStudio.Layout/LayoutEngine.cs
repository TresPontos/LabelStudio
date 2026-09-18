using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Layout;

public sealed class LayoutEngine
{
    public PreparedScene Prepare(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<PreparedElement> prepared = [];
        foreach (DocumentElement element in document.Elements)
        {
            prepared.Add(PrepareElement(element));
        }

        return new PreparedScene(
            document.PageDimensions,
            document.MediaGeometry.PrintableArea,
            prepared.AsReadOnly());
    }

    private static PreparedElement PrepareElement(DocumentElement element)
    {
        return element switch
        {
            RectangleElement rect => new PreparedElement(
                rect.Id, rect.Bounds, rect.Ink, PreparedContent.Rectangle())
            {
                IsFilled = rect.Fill,
                StrokeWidth = rect.StrokeWidth,
            },
            LineElement line => new PreparedElement(
                line.Id, line.Bounds, line.Ink, PreparedContent.Line())
            {
                LineThickness = line.Thickness,
            },
            ImageElement img => new PreparedElement(
                img.Id, img.Bounds, img.Ink, PreparedContent.Image(img.AssetId)),
            TextElement text => new PreparedElement(
                text.Id, text.Bounds, text.Ink, PreparedContent.Text(text.Text)),
            _ => throw new NotSupportedException($"Element type {element.GetType().Name} is not supported."),
        };
    }
}