using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public sealed record LabelDocument(
    DocumentId Id,
    int FormatVersion,
    PhysicalSize PageDimensions,
    DocumentMediaKind MediaKind,
    string MediaProfileId,
    MediaSnapshot MediaGeometry,
    IReadOnlyList<DocumentElement> Elements,
    DocumentPrintDefaults PrintDefaults,
    DocumentDesignMetadata DesignMetadata)
{
    public const int CurrentFormatVersion = 4;

    public static LabelDocument Create(
        PhysicalSize pageDimensions,
        string mediaProfileId,
        MediaSnapshot mediaGeometry,
        IEnumerable<DocumentElement>? elements = null,
        DocumentPrintDefaults? printDefaults = null,
        DocumentDesignMetadata? designMetadata = null,
        DocumentMediaKind mediaKind = DocumentMediaKind.DieCut)
        => new(
            DocumentId.New(),
            CurrentFormatVersion,
            pageDimensions,
            mediaKind,
            mediaProfileId,
            mediaGeometry,
            (elements ?? []).ToList().AsReadOnly(),
            printDefaults ?? DocumentPrintDefaults.Default,
            designMetadata ?? DocumentDesignMetadata.CreateDefault(pageDimensions));

    public LabelDocument WithElements(IReadOnlyList<DocumentElement> newElements) =>
        this with { Elements = newElements };
}
