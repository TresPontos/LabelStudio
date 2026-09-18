namespace LabelStudio.Printing.BrotherQl;

public sealed record BrotherQlMediaMapping(
    string ProfileId,
    byte PrintInformationMediaType,
    byte StatusMediaType,
    int ProtocolWidthMillimetres,
    int ProtocolLengthMillimetres,
    int HeadLeftBlankDots,
    int PrintableWidthDots,
    int PrintableLengthDots,
    int MinimumFeedMarginDots,
    int MaximumFeedMarginDots,
    bool RequiresTwoColourRaster)
{
    public int HeadRightBlankDots => 720 - HeadLeftBlankDots - PrintableWidthDots;

    public static BrotherQlMediaMapping Dk22251() => new(
        "brother.dk-22251",
        0x0A, 0x4A,
        62, 0,
        12, 696, 0,
        35, 1500,
        true);

    public static BrotherQlMediaMapping Dk11204() => new(
        "brother.dk-11204",
        0x0B, 0x4B,
        17, 54,
        555, 165, 566,
        0, 0,
        false);

    public static IReadOnlyList<BrotherQlMediaMapping> BuiltIn() => [Dk22251(), Dk11204()];

    public static BrotherQlMediaMapping For(string profileId) =>
        BuiltIn().FirstOrDefault(m => m.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"No Brother QL mapping for profile '{profileId}'.");
}