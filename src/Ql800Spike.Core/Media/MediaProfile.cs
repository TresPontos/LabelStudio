namespace Ql800Spike.Core.Media;

public sealed record MediaProfile(
    string ProfileId,
    string Sku,
    string DisplayName,
    MediaKind Kind,
    int PhysicalWidthMicrometres,
    int? PhysicalLengthMicrometres,
    PrintableArea PrintableArea,
    IReadOnlyList<ThermalInk> SupportedInks,
    IReadOnlyList<string> CompatiblePrinterModels,
    CutterConstraints Cutter,
    BrotherQlMediaMapping BrotherQl);

public sealed record PrintableArea(
    int CrossFeedOffsetMicrometres,
    int FeedOffsetMicrometres,
    int WidthMicrometres,
    int? LengthMicrometres);

public sealed record CutterConstraints(
    bool SupportsAutomaticCut,
    int? MinimumContinuousLengthMicrometres,
    int? MaximumContinuousLengthMicrometres);

public sealed record BrotherQlMediaMapping(
    int ReferenceMediaId,
    byte PrintInformationMediaType,
    byte StatusMediaType,
    byte ProtocolWidthMillimetres,
    byte ProtocolLengthMillimetres,
    int PhysicalWidthDots,
    int? PhysicalLengthDots,
    int HeadLeftBlankDots,
    int PrintableWidthDots,
    int HeadRightBlankDots,
    int? PrintableLengthDots,
    int FeedOffsetDots,
    int MinimumFeedMarginDots,
    int MaximumFeedMarginDots,
    bool RequiresTwoColourRaster);
