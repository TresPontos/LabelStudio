using LabelStudio.Document.Units;

namespace LabelStudio.Layout;

public interface ITextMeasurer
{
    MicrometreRect Measure(string text, int fontSizePoints, string? fontFamily);
}