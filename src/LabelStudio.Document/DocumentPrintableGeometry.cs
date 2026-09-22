using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public static class DocumentPrintableGeometry
{
    public static MicrometreRect GetMediaPrintableArea(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        MicrometreRect area = document.MediaGeometry.PrintableArea;
        if (document.MediaKind != DocumentMediaKind.Continuous || area.Height > Micrometre.Zero)
        {
            return area;
        }

        int height = Math.Max(0, document.PageDimensions.Height.Value - area.Y.Value);
        return area with { Height = new Micrometre(height) };
    }

    public static MicrometreRect GetSafeArea(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        MicrometreRect printable = GetMediaPrintableArea(document);
        DocumentSafeMargins margins = document.DesignMetadata.SafeMargins;
        int width = Math.Max(0,
            printable.Width.Value - margins.Left.Value - margins.Right.Value);
        int height = Math.Max(0,
            printable.Height.Value - margins.Top.Value - margins.Bottom.Value);
        return new MicrometreRect(
            printable.X + margins.Left,
            printable.Y + margins.Top,
            new Micrometre(width),
            new Micrometre(height));
    }
}
