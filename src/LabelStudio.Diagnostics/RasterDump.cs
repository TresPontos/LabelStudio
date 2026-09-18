using System.Text.Json;
using System.Text.Json.Nodes;
using LabelStudio.Printing;
using LabelStudio.Rendering;

namespace LabelStudio.Diagnostics;

public static class RasterDump
{
    public static void Dump(RenderedPlanes planes, string outputDirectory, string prefix = "")
    {
        Directory.CreateDirectory(outputDirectory);

        string blackPath = Path.Combine(outputDirectory,
            string.IsNullOrEmpty(prefix) ? "black-plane.pbm" : $"{prefix}-black-plane.pbm");
        File.WriteAllBytes(blackPath, planes.BlackPlane.ToPbmBytes());

        if (planes.RedPlane is not null)
        {
            string redPath = Path.Combine(outputDirectory,
                string.IsNullOrEmpty(prefix) ? "red-plane.pbm" : $"{prefix}-red-plane.pbm");
            File.WriteAllBytes(redPath, planes.RedPlane.ToPbmBytes());
        }

        JsonObject summary = new()
        {
            ["blackPlaneWidth"] = planes.BlackPlane.Width,
            ["blackPlaneHeight"] = planes.BlackPlane.Height,
            ["hasRedPlane"] = planes.RedPlane is not null,
            ["dumpedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        File.WriteAllText(Path.Combine(outputDirectory,
            string.IsNullOrEmpty(prefix) ? "raster-dump.json" : $"{prefix}-raster-dump.json"),
            summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}