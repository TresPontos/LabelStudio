using LabelStudio.Document.Ink;

namespace LabelStudio.Document;

public sealed record DocumentPrintDefaults(
    InkChannel DefaultInk,
    bool AutoCut,
    bool CutAtEnd,
    int Dpi)
{
    public static DocumentPrintDefaults Default => new(InkChannel.Black, true, true, 300);
}