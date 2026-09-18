namespace LabelStudio.Document.Units;

public readonly record struct Micrometre(int Value)
{
    public static Micrometre Zero => new(0);

    public static Micrometre FromMillimetres(double millimetres) =>
        new((int)Math.Round(millimetres * 1000.0, MidpointRounding.AwayFromZero));

    public double ToMillimetres() => Value / 1000.0;

    public Micrometre Clamp(Micrometre min, Micrometre max) =>
        Value < min.Value ? min : Value > max.Value ? max : this;

    public static Micrometre operator +(Micrometre a, Micrometre b) => new(a.Value + b.Value);
    public static Micrometre operator -(Micrometre a, Micrometre b) => new(a.Value - b.Value);
    public static Micrometre operator -(Micrometre a) => new(-a.Value);
    public static bool operator >(Micrometre a, Micrometre b) => a.Value > b.Value;
    public static bool operator <(Micrometre a, Micrometre b) => a.Value < b.Value;
    public static bool operator >=(Micrometre a, Micrometre b) => a.Value >= b.Value;
    public static bool operator <=(Micrometre a, Micrometre b) => a.Value <= b.Value;

    public static Micrometre Min(Micrometre a, Micrometre b) => a.Value <= b.Value ? a : b;
    public static Micrometre Max(Micrometre a, Micrometre b) => a.Value >= b.Value ? a : b;

    public override string ToString() => $"{Value} um";
}