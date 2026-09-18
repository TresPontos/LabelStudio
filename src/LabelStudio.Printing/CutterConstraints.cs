using LabelStudio.Document.Units;

namespace LabelStudio.Printing;

public sealed record CutterConstraints(
    bool SupportsAutomaticCut,
    Micrometre? MinimumContinuousLength,
    Micrometre? MaximumContinuousLength)
{
    public static CutterConstraints Continuous(bool supportsCut, Micrometre min, Micrometre max) =>
        new(supportsCut, min, max);

    public static CutterConstraints DieCut(bool supportsCut) =>
        new(supportsCut, null, null);
}