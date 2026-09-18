namespace LabelStudio.Document.Units;

public static class PhysicalUnits
{
    public const int MicrometresPerMillimetre = 1000;
    public const int MicrometresPerInch = 25_400;

    public static int MicrometresToDots(int micrometres, int dpi)
    {
        double dots = (double)micrometres * dpi / MicrometresPerInch;
        return (int)Math.Round(dots, MidpointRounding.AwayFromZero);
    }

    public static int DotsToMicrometres(int dots, int dpi)
    {
        double micrometres = (double)dots * MicrometresPerInch / dpi;
        return (int)Math.Round(micrometres, MidpointRounding.AwayFromZero);
    }

    public static int MillimetresToMicrometres(double millimetres) =>
        (int)Math.Round(millimetres * MicrometresPerMillimetre, MidpointRounding.AwayFromZero);

    public static double MicrometresToMillimetres(int micrometres) =>
        micrometres / (double)MicrometresPerMillimetre;

    public static int MillimetresToDots(double millimetres, int dpi) =>
        MicrometresToDots(MillimetresToMicrometres(millimetres), dpi);
}