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

        BrotherQlMediaMapping mapping = BrotherQlMediaMapping.For(job.Media.ProfileId);

        int feedMargin = job.Media.Kind == MediaKind.Continuous ? 35 : 0;

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

        QlEncodedJob encoded = Encode(job);

        RawSubmissionResult result = _transport.Submit(
            _transportTarget,
            encoded.Payload,
            CancellationToken.None);

        return result.Success
            ? PrintResult.Succeeded(result.SpoolerJobId?.ToString())
            : PrintResult.Failed(result.Error ?? "Unknown transport error.");
    }
}