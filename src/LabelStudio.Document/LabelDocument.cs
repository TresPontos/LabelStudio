using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public sealed record LabelDocument(
    DocumentId Id,
    int FormatVersion,
    PhysicalSize PageDimensions,
    string MediaProfileId,
    MediaSnapshot MediaGeometry,
    IReadOnlyList<DocumentElement> Elements,
    DocumentPrintDefaults PrintDefaults)
{
    public const int CurrentFormatVersion = 1;

    public static LabelDocument Create(
        PhysicalSize pageDimensions,
        string mediaProfileId,
        MediaSnapshot mediaGeometry,
        IEnumerable<DocumentElement>? elements = null,
        DocumentPrintDefaults? printDefaults = null)
        => new(
            DocumentId.New(),
            CurrentFormatVersion,
            pageDimensions,
            mediaProfileId,
            mediaGeometry,
            (elements ?? []).ToList().AsReadOnly(),
            printDefaults ?? DocumentPrintDefaults.Default);

    public LabelDocument WithElements(IReadOnlyList<DocumentElement> newElements) =>
        this with { Elements = newElements };
}