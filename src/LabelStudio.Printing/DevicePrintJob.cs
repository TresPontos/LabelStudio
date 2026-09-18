using LabelStudio.Document;
using LabelStudio.Rendering;

namespace LabelStudio.Printing;

public sealed record DevicePrintJob(
    PrintIntent Intent,
    RenderedPlanes Planes,
    MediaProfile Media,
    PrintSettings Settings,
    string JobName)
{
    public DocumentId DocumentId => Intent.DocumentId;
}