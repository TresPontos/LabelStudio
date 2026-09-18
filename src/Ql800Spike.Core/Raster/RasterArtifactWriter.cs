using System.Text;

namespace Ql800Spike.Core.Raster;

public static class RasterArtifactWriter
{
    public static void WritePortableBitmap(MonochromeRaster raster, Stream output)
    {
        ArgumentNullException.ThrowIfNull(raster);
        ArgumentNullException.ThrowIfNull(output);

        byte[] header = Encoding.ASCII.GetBytes($"P4\n{raster.Width} {raster.Height}\n");
        output.Write(header);
        output.Write(raster.Data.Span);
    }
}
