using Ql800Spike.Core.Media;

namespace Ql800Spike.Core.Raster;

public sealed record GeneratedTestPattern(
    string TestId,
    MediaProfile Media,
    MonochromeRaster Raster,
    int FeedMarginDots,
    int? RequestedFinishedLengthMicrometres,
    string LengthPlanningNote);

public sealed record GeneratedColourTestPattern(
    string TestId,
    MediaProfile Media,
    MonochromeRaster BlackRaster,
    MonochromeRaster RedRaster,
    int FeedMarginDots,
    int RequestedFinishedLengthMicrometres,
    string LengthPlanningNote);
