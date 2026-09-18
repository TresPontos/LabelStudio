using LabelStudio.Document.Units;

namespace LabelStudio.Printing;

public sealed record PrintableArea(
    Micrometre CrossFeedOffset,
    Micrometre FeedOffset,
    Micrometre Width,
    Micrometre? Length)
{
    public static PrintableArea Continuous(Micrometre crossFeedOffset, Micrometre width) =>
        new(crossFeedOffset, Micrometre.Zero, width, null);

    public static PrintableArea DieCut(Micrometre crossFeedOffset, Micrometre feedOffset, Micrometre width, Micrometre length) =>
        new(crossFeedOffset, feedOffset, width, length);
}