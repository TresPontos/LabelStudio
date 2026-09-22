using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public readonly record struct DocumentSafeMargins(
    Micrometre Top,
    Micrometre Right,
    Micrometre Bottom,
    Micrometre Left)
{
    public static DocumentSafeMargins Uniform(Micrometre margin) =>
        new(margin, margin, margin, margin);

    public static DocumentSafeMargins Default { get; } = Uniform(Micrometre.FromMillimetres(1));
}
