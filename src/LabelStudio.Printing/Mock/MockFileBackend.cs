using System.Text.Json;
using System.Text.Json.Nodes;
using LabelStudio.Printing;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.Mock;

public sealed class MockFileBackend : IPrinterBackend
{
    public PrinterDescriptor Descriptor { get; } = new("mock", "Mock/File Backend", "LabelStudio", "Mock");
    public PrinterCapabilities Capabilities { get; } = new(
        true, true,
        [new PrinterResolution(300, 300), new PrinterResolution(300, 600)],
        ["brother.dk-22251", "brother.dk-11204"]);

    public string OutputDirectory { get; }

    public MockFileBackend(string outputDirectory)
    {
        OutputDirectory = outputDirectory;
    }

    public PrintResult Print(DevicePrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        try
        {
            Directory.CreateDirectory(OutputDirectory);
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            string dir = Path.Combine(OutputDirectory, $"{timestamp}-{job.DocumentId}");
            Directory.CreateDirectory(dir);

            WriteManifest(job, dir);
            WriteRasterPng(job.Planes.BlackPlane, Path.Combine(dir, "black-plane.png"));
            if (job.Planes.RedPlane is not null)
            {
                WriteRasterPng(job.Planes.RedPlane, Path.Combine(dir, "red-plane.png"));
            }

            return PrintResult.Succeeded(null);
        }
        catch (Exception ex)
        {
            return PrintResult.Failed(ex.Message);
        }
    }

    private static void WriteManifest(DevicePrintJob job, string dir)
    {
        JsonObject manifest = new()
        {
            ["documentId"] = job.DocumentId.ToString(),
            ["jobName"] = job.JobName,
            ["mediaProfileId"] = job.Media.ProfileId,
            ["mediaSku"] = job.Media.Sku,
            ["dpi"] = job.Settings.Dpi,
            ["autoCut"] = job.Settings.AutoCut,
            ["cutAtEnd"] = job.Settings.CutAtEnd,
            ["ink"] = job.Settings.Ink.ToString(),
            ["hasRedPlane"] = job.Planes.RedPlane is not null,
            ["blackPlaneWidth"] = job.Planes.BlackPlane.Width,
            ["blackPlaneHeight"] = job.Planes.BlackPlane.Height,
            ["labelWidthMicrometres"] = job.Intent.Scene.LabelSize.Width.Value,
            ["labelLengthMicrometres"] = job.Intent.Scene.LabelSize.Height.Value,
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteRasterPng(MonochromeRaster raster, string path)
    {
        using FileStream fs = File.Create(path);
        using System.IO.BinaryWriter writer = new(fs);
        writer.Write(raster.ToPbmBytes());
    }
}
