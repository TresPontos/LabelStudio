namespace LabelStudio.Document.Units;

public readonly record struct MicrometreRect(
    Micrometre X,
    Micrometre Y,
    Micrometre Width,
    Micrometre Height)
{
    public static MicrometreRect Zero => new(Micrometre.Zero, Micrometre.Zero, Micrometre.Zero, Micrometre.Zero);

    public Micrometre Right => X + Width;
    public Micrometre Bottom => Y + Height;
    public Micrometre CentreX => X + new Micrometre(Width.Value / 2);
    public Micrometre CentreY => Y + new Micrometre(Height.Value / 2);

    public bool Contains(MicrometrePoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public bool Intersects(MicrometreRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public MicrometreRect Inflate(Micrometre amount) =>
        new(X - amount, Y - amount, Width + amount + amount, Height + amount + amount);

    public MicrometreRect Offset(Micrometre dx, Micrometre dy) =>
        new(X + dx, Y + dy, Width, Height);
}