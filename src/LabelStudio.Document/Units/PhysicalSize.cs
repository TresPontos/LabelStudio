namespace LabelStudio.Document.Units;

public readonly record struct PhysicalSize(Micrometre Width, Micrometre Height)
{
    public static PhysicalSize Zero => new(Micrometre.Zero, Micrometre.Zero);

    public static PhysicalSize FromMillimetres(double widthMm, double heightMm) =>
        new(Micrometre.FromMillimetres(widthMm), Micrometre.FromMillimetres(heightMm));
}
