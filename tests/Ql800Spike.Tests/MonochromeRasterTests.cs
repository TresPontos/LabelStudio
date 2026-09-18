using Ql800Spike.Core.Raster;

namespace Ql800Spike.Tests;

public sealed class MonochromeRasterTests
{
    [Fact]
    public void PixelsUseMostSignificantBitFirst()
    {
        MonochromeRaster raster = new(16, 1);

        raster.SetPixel(0, 0);
        raster.SetPixel(7, 0);
        raster.SetPixel(8, 0);
        raster.SetPixel(15, 0);

        Assert.Equal([0x81, 0x81], raster.Data.ToArray());
    }

    [Fact]
    public void PortableBitmapContainsExactRasterBytes()
    {
        MonochromeRaster raster = new(8, 1);
        raster.SetPixel(0, 0);

        using MemoryStream output = new();
        RasterArtifactWriter.WritePortableBitmap(raster, output);

        Assert.Equal("P4\n8 1\n", System.Text.Encoding.ASCII.GetString(output.ToArray()[..7]));
        Assert.Equal(0x80, output.ToArray()[7]);
    }
}
