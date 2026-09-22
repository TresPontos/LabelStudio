using LabelStudio.Printing;
using LabelStudio.Rendering;

namespace LabelStudio.Printing.BrotherQl;

public sealed class Ql800PrinterBackend : IPrinterBackend
{
    private readonly IRawPrinterTransport? _transport;
    private readonly PrinterTransportTarget? _transportTarget;
    private readonly QlRasterEncoder _encoder = new();
    private readonly QlRasterValidator _validator = new();

    public PrinterDescriptor Descriptor { get; }
    public PrinterCapabilities Capabilities { get; }

    public Ql800PrinterBackend(
        PrinterDescriptor? descriptor = null,
        IRawPrinterTransport? transport = null,
        PrinterTransportTarget? transportTarget = null)
    {
        _transport = transport;
        _transportTarget = transportTarget;
        Descriptor = descriptor ?? new PrinterDescriptor(
            "ql800", "Brother QL-800", "Brother", "QL-800");
        Capabilities = new PrinterCapabilities(
            true, true,
            [new PrinterResolution(300, 300), new PrinterResolution(300, 600)],
            ["brother.dk-22251", "brother.dk-11204"]);
    }

    public QlEncodedJob Encode(DevicePrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Settings.Dpi != QlContinuousLengthPlanner.Dpi)
        {
            throw new ArgumentException(
                $"QL raster jobs require {QlContinuousLengthPlanner.Dpi} DPI, got {job.Settings.Dpi} DPI.",
                nameof(job));
        }

        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(job.Media.ProfileId);

        int feedMargin = 0;
        if (job.Media.Kind == MediaKind.Continuous)
        {
            QlContinuousLengthPlan lengthPlan = QlContinuousLengthPlanner.Plan(
                job.Intent.Scene.LabelSize.Height,
                mapping);
            feedMargin = lengthPlan.FeedMarginDots;
            if (job.Planes.BlackPlane.Height != lengthPlan.RasterRows)
            {
                throw new ArgumentException(
                    $"Continuous cut length requires {lengthPlan.RasterRows} raster rows, got {job.Planes.BlackPlane.Height}.",
                    nameof(job));
            }
        }

        QlRasterJobOptions options = new(
            job.Media,
            mapping,
            job.Planes.BlackPlane,
            job.Planes.RedPlane,
            feedMargin,
            job.Settings.AutoCut,
            1,
            job.Settings.CutAtEnd);

        QlEncodedJob encoded = _encoder.Encode(options);

        QlValidationResult validation = _validator.Validate(encoded.Payload, job.Media, mapping);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "QL raster validation failed: " + string.Join(" | ", validation.Errors));
        }

        return encoded;
    }

    public PrintResult Print(DevicePrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (_transport is null || _transportTarget is null)
        {
            return PrintResult.Failed("No transport configured. Use --printer option to specify a queue.");
        }

        try
        {
            QlEncodedJob encoded = Encode(job);
            RawSubmissionResult result = _transport.Submit(
                _transportTarget,
                encoded.Payload,
                CancellationToken.None);

            return result.Success
                ? PrintResult.Succeeded(result.SpoolerJobId?.ToString())
                : PrintResult.Failed(result.Error ??
                    "The raw printer transport returned failure without an error message.");
        }
        catch (Exception ex)
        {
            return PrintResult.Failed(
                $"Print pipeline failed ({ex.GetType().Name}): {ex.Message}");
        }
    }
}
