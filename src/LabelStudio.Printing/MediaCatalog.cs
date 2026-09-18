using LabelStudio.Document.Units;

namespace LabelStudio.Printing;

public sealed class MediaCatalog
{
    private readonly Dictionary<string, MediaProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public void Register(MediaProfile profile)
    {
        _profiles[profile.ProfileId] = profile;
    }

    public MediaProfile Get(string profileId)
    {
        return _profiles.TryGetValue(profileId, out MediaProfile? profile)
            ? profile
            : throw new KeyNotFoundException($"Media profile '{profileId}' not found.");
    }

    public bool TryGet(string profileId, out MediaProfile? profile) =>
        _profiles.TryGetValue(profileId, out profile);

    public IReadOnlyList<MediaProfile> All => _profiles.Values.ToList().AsReadOnly();

    public static MediaCatalog CreateBuiltIn()
    {
        MediaCatalog catalog = new();
        RegisterDk22251(catalog);
        RegisterDk11204(catalog);
        return catalog;
    }

    private static void RegisterDk22251(MediaCatalog catalog)
    {
        catalog.Register(new MediaProfile(
            "brother.dk-22251",
            "DK-22251",
            "62 mm Continuous Black/Red on White",
            MediaKind.Continuous,
            62_000,
            null,
            PrintableArea.Continuous(new(1500), new(58_900)),
            [ThermalInk.Black, ThermalInk.Red],
            ["QL-800", "QL-810W", "QL-820NWB"],
            CutterConstraints.Continuous(true, new(12_700), new(1_000_000))));
    }

    private static void RegisterDk11204(MediaCatalog catalog)
    {
        catalog.Register(new MediaProfile(
            "brother.dk-11204",
            "DK-11204",
            "17 x 54 mm Die-Cut Black on White",
            MediaKind.DieCut,
            17_000,
            53_900,
            PrintableArea.DieCut(new(1500), new(3000), new(14_000), new(47_900)),
            [ThermalInk.Black],
            ["QL-800", "QL-810W", "QL-820NWB"],
            CutterConstraints.DieCut(true)));
    }
}