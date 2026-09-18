using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public sealed record MediaSnapshot(
    string ProfileId,
    PhysicalSize PhysicalDimensions,
    MicrometreRect PrintableArea)
{
    public static MediaSnapshot FromProfile(string profileId, PhysicalSize dimensions, MicrometreRect printable) =>
        new(profileId, dimensions, printable);
}