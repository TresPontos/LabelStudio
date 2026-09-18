using LabelStudio.Printing;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.BrotherQl;

public sealed record QlRasterJobOptions(
    MediaProfile Media,
    BrotherQlMediaMapping Mapping,
    MonochromeRaster BlackRaster,
    MonochromeRaster? RedRaster,
    int FeedMarginDots,
    bool AutoCut = true,
    byte CutEvery = 1,
    bool CutAtEnd = true,
    bool HighResolution = false);

public sealed record QlEncodedJob(
    byte[] Payload,
    string MediaProfileId,
    int RasterLineCount,
    bool TwoColour,
    int FeedMarginDots,
    bool AutoCut,
    byte CutEvery,
    bool CutAtEnd);