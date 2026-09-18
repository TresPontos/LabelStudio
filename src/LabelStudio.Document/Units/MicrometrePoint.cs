namespace LabelStudio.Document.Units;

public readonly record struct MicrometrePoint(Micrometre X, Micrometre Y)
{
    public static MicrometrePoint Zero => new(Micrometre.Zero, Micrometre.Zero);

    public MicrometrePoint Offset(Micrometre dx, Micrometre dy) => new(X + dx, Y + dy);
}