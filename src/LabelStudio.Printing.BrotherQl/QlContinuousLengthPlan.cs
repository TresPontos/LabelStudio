using LabelStudio.Document.Units;

namespace LabelStudio.Printing.BrotherQl;

public sealed record QlContinuousLengthPlan(
    Micrometre CutLength,
    int LengthDots,
    int FeedMarginDots,
    int RasterRows);

public static class QlContinuousLengthPlanner
{
    public const int Dpi = 300;
    public const int MinimumCutLengthMicrometres = 12_700;
    public const int MaximumCutLengthMicrometres = 1_000_000;

    public static QlContinuousLengthPlan Plan(
        Micrometre cutLength,
        BrotherQlMediaMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (cutLength.Value is < MinimumCutLengthMicrometres or > MaximumCutLengthMicrometres)
        {
            throw new ArgumentOutOfRangeException(nameof(cutLength),
                $"Continuous cut length must be {MinimumCutLengthMicrometres}..{MaximumCutLengthMicrometres} micrometres.");
        }
        if (mapping.MinimumFeedMarginDots <= 0)
        {
            throw new ArgumentException("The media mapping is not continuous media.", nameof(mapping));
        }

        int lengthDots = PhysicalUnits.MicrometresToDots(cutLength.Value, Dpi);
        int feedMargin = mapping.MinimumFeedMarginDots;
        int rasterRows = lengthDots - (2 * feedMargin);
        if (rasterRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cutLength),
                "Continuous cut length leaves no printable raster rows after feed margins.");
        }

        return new QlContinuousLengthPlan(cutLength, lengthDots, feedMargin, rasterRows);
    }
}
