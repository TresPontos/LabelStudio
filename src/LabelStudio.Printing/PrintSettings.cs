using LabelStudio.Document.Ink;

namespace LabelStudio.Printing;

public sealed record PrintSettings(
    InkChannel Ink,
    bool AutoCut,
    bool CutAtEnd,
    int Dpi)
{
    public static PrintSettings Default => new(InkChannel.Black, true, true, 300);
}