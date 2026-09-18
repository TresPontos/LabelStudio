using LabelStudio.Document;
using LabelStudio.Layout;

namespace LabelStudio.Printing;

public sealed record PrintIntent(
    DocumentId DocumentId,
    PreparedScene Scene,
    MediaProfile Media,
    PrintSettings Settings);