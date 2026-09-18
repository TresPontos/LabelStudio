namespace Ql800Spike.Core.Geometry;

public static class PhysicalUnits
{
    public const int MicrometresPerMillimetre = 1_000;
    public const int MicrometresPerInch = 25_400;

    public static int MillimetresToMicrometres(decimal millimetres) =>
        checked((int)decimal.Round(
            millimetres * MicrometresPerMillimetre,
            0,
            MidpointRounding.AwayFromZero));

    public static int MicrometresToDots(int micrometres, int dotsPerInch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(micrometres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dotsPerInch);

        return checked((int)decimal.Round(
            (decimal)micrometres * dotsPerInch / MicrometresPerInch,
            0,
            MidpointRounding.AwayFromZero));
    }

    public static decimal DotsToMillimetres(int dots, int dotsPerInch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dotsPerInch);

        return (decimal)dots * MicrometresPerInch /
            dotsPerInch / MicrometresPerMillimetre;
    }
}
