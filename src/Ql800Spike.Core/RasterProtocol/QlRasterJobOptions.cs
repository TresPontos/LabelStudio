using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;

namespace Ql800Spike.Core.RasterProtocol;

public sealed record QlRasterJobOptions(
    MediaProfile Media,
    MonochromeRaster BlackRaster,
    int FeedMarginDots,
    MonochromeRaster? RedRaster = null,
    bool AutoCut = true,
    byte CutEvery = 1,
    bool CutAtEnd = true,
    bool HighResolution = false);

public sealed record QlRasterJob(
    byte[] Payload,
    string MediaProfileId,
    int RasterLineCount,
    int PlaneBytesPerRasterLine,
    int EncodedBytesPerRasterLine,
    int FeedMarginDots,
    bool TwoColour,
    bool AutoCut,
    byte CutEvery,
    bool CutAtEnd,
    bool HighResolution);
