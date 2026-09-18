using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ql800Spike.Core.Diagnostics;
using Ql800Spike.Core.Discovery;
using Ql800Spike.Core.Media;
using Ql800Spike.Core.Raster;
using Ql800Spike.Core.RasterProtocol;
using Ql800Spike.Core.Status;
using Ql800Spike.Core.Spooler;

return SpikeCli.Run(args);

internal static class SpikeCli
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "environment" => CaptureEnvironment(args[1..]),
                "discover" => Discover(args[1..]),
                "status" => ShowWindowsStatus(args[1..]),
                "list-media" => ListMedia(),
                "generate" => Generate(args[1..]),
                "decode-status" => DecodeStatus(args[1..]),
                "probe-status" => ProbeBrotherStatus(args[1..]),
                "probe-status-worker" => RunStatusProbeWorker(args[1..]),
                "print-raw-canary" => PrintRawPhysicalTest(args[1..], PhysicalTestKind.Continuous),
                "print-raw-colour-test" => PrintRawPhysicalTest(args[1..], PhysicalTestKind.ColourPlanes),
                "print-raw-diecut-test" => PrintRawPhysicalTest(args[1..], PhysicalTestKind.DieCut),
                "raw-print-worker" => RunRawPrintWorker(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            if (HasFlag(args, "--verbose"))
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static int CaptureEnvironment(string[] args)
    {
        string path = GetOption(args, "--output") ??
            Path.Combine(CreateRunDirectory("environment"), "environment.json");
        EnsureParentDirectory(path);
        WriteJson(path, EnvironmentManifest.Capture());
        Console.WriteLine($"Environment manifest: {Path.GetFullPath(path)}");
        return 0;
    }

    private static int Discover(string[] args)
    {
        string directory = GetOption(args, "--output") ?? CreateRunDirectory("discovery");
        Directory.CreateDirectory(directory);

        WindowsPrinterDiscovery discovery = new();
        PrinterDiscoveryReport report = discovery.Discover();
        WriteJson(Path.Combine(directory, "environment.json"), EnvironmentManifest.Capture());
        WriteJson(Path.Combine(directory, "discovery.json"), report);

        List<CapabilityResult> capabilityResults = [];
        foreach (PrinterQueueInfo queue in report.Queues)
        {
            if (!IsBrotherCandidate(queue))
            {
                capabilityResults.Add(new CapabilityResult(
                    queue.Name,
                    null,
                    "Skipped: queue does not look like a Brother printer."));
                continue;
            }

            try
            {
                capabilityResults.Add(new CapabilityResult(
                    queue.Name,
                    discovery.GetCapabilities(queue),
                    null));
            }
            catch (Exception exception)
            {
                capabilityResults.Add(new CapabilityResult(queue.Name, null, exception.Message));
            }
        }

        WriteJson(Path.Combine(directory, "capabilities.json"), capabilityResults);
        PrintDiscoverySummary(report, capabilityResults);
        Console.WriteLine();
        Console.WriteLine($"Artifacts: {Path.GetFullPath(directory)}");
        Console.WriteLine("This command performed read-only spooler and PnP queries. It sent no printer data.");
        return 0;
    }

    private static int ShowWindowsStatus(string[] args)
    {
        string printerName = RequireOption(args, "--printer");
        WindowsPrinterDiscovery discovery = new();
        PrinterQueueInfo queue = discovery.EnumerateQueues().FirstOrDefault(
                item => string.Equals(item.Name, printerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Printer queue '{printerName}' was not found.");
        PrinterCapabilities capabilities = discovery.GetCapabilities(queue);

        Console.WriteLine($"Printer: {queue.Name}");
        Console.WriteLine($"Driver: {queue.DriverName} ({queue.Driver?.FileVersion ?? "version unavailable"})");
        Console.WriteLine($"Port: {queue.PortName}");
        Console.WriteLine($"Windows status: {queue.Status}");
        Console.WriteLine($"Queued jobs: {queue.Jobs}");
        Console.WriteLine("Driver-reported ready media:");
        if (capabilities.DriverReportedReadyMedia.Count == 0)
        {
            Console.WriteLine("  <none reported; this does not prove that no roll is installed>");
        }
        else
        {
            foreach (string media in capabilities.DriverReportedReadyMedia)
            {
                Console.WriteLine($"  {media}");
            }
        }

        Console.WriteLine("No Brother raster status request was sent.");
        return 0;
    }

    private static int ListMedia()
    {
        foreach (MediaProfile media in MediaCatalog.LoadBuiltIn().Profiles)
        {
            Console.WriteLine($"{media.ProfileId}");
            Console.WriteLine($"  SKU: {media.Sku}");
            Console.WriteLine($"  Kind: {media.Kind}");
            Console.WriteLine($"  Physical: {media.PhysicalWidthMicrometres / 1000m:0.###} x " +
                $"{(media.PhysicalLengthMicrometres is int length ? $"{length / 1000m:0.###} mm" : "continuous")}");
            Console.WriteLine($"  Printable head dots: {media.BrotherQl.HeadLeftBlankDots} / " +
                $"{media.BrotherQl.PrintableWidthDots} / {media.BrotherQl.HeadRightBlankDots}");
            Console.WriteLine($"  Inks: {string.Join(", ", media.SupportedInks)}");
        }

        return 0;
    }

    private static int Generate(string[] args)
    {
        string testId = RequireOption(args, "--test");
        string defaultMedia = testId.Equals("diecut-placement", StringComparison.OrdinalIgnoreCase)
            ? "brother.dk-11204"
            : "brother.dk-22251";
        string mediaId = GetOption(args, "--media") ?? defaultMedia;

        MediaProfile media = MediaCatalog.LoadBuiltIn().GetRequired(mediaId);
        TestPatternGenerator generator = new();
        GeneratedTestPattern pattern = generator.Generate(testId, media);
        QlRasterJobEncoder encoder = new();
        QlRasterJob job = encoder.Encode(new QlRasterJobOptions(
            media,
            pattern.Raster,
            pattern.FeedMarginDots));

        QlRasterValidationResult validation = new QlRasterJobValidator().Validate(job.Payload, media);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "Generated payload failed dry validation: " + string.Join(" | ", validation.Errors));
        }

        string directory = GetOption(args, "--output") ?? CreateRunDirectory($"dry-{testId}");
        Directory.CreateDirectory(directory);

        string rasterPath = Path.Combine(directory, "source-raster.pbm");
        using (FileStream output = File.Create(rasterPath))
        {
            RasterArtifactWriter.WritePortableBitmap(pattern.Raster, output);
        }

        string payloadPath = Path.Combine(directory, "ql-raster-job.bin");
        File.WriteAllBytes(payloadPath, job.Payload);

        DryGenerationManifest manifest = new(
            DateTimeOffset.UtcNow,
            pattern.TestId,
            media.ProfileId,
            media.Sku,
            pattern.Raster.Width,
            pattern.Raster.Height,
            TestPatternGenerator.StandardDpi,
            TestPatternGenerator.StandardDpi,
            pattern.FeedMarginDots,
            pattern.RequestedFinishedLengthMicrometres,
            pattern.LengthPlanningNote,
            job.Payload.Length,
            Sha256(pattern.Raster.Data.Span),
            Sha256(job.Payload),
            validation);
        WriteJson(Path.Combine(directory, "manifest.json"), manifest);

        Console.WriteLine($"Test: {pattern.TestId}");
        Console.WriteLine($"Media: {media.Sku} ({media.ProfileId})");
        Console.WriteLine($"Raster: {pattern.Raster.Width} x {pattern.Raster.Height} dots");
        Console.WriteLine($"Payload: {job.Payload.Length} bytes");
        Console.WriteLine($"Validation: PASS ({validation.RasterLineCount} raster rows)");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(directory)}");
        Console.WriteLine("The payload was generated and validated only. Nothing was sent to a printer.");
        return 0;
    }

    private static int DecodeStatus(string[] args)
    {
        string hex = RequireOption(args, "--hex");
        byte[] bytes = Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
        QlPrinterStatus status = new QlStatusDecoder().Decode(bytes);
        MediaIdentification media = MediaCatalog.LoadBuiltIn().Identify(status.Media);

        Console.WriteLine(JsonSerializer.Serialize(new { status, media }, JsonOptions));
        return 0;
    }

    private static int ProbeBrotherStatus(string[] args)
    {
        const string confirmationFlag = "--confirm-nonprinting-status-request";
        if (!HasFlag(args, confirmationFlag))
        {
            throw new ArgumentException(
                $"The documented three-byte Brother status request requires {confirmationFlag}.");
        }

        string printerName = RequireOption(args, "--printer");
        int timeoutMilliseconds = int.TryParse(GetOption(args, "--timeout-ms"), out int parsedTimeout)
            ? parsedTimeout
            : 10_000;
        if (timeoutMilliseconds is < 1_000 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Timeout must be 1000..60000 milliseconds.");
        }

        WindowsPrinterDiscovery discovery = new();
        PrinterQueueInfo queue = discovery.EnumerateQueues().FirstOrDefault(
                item => string.Equals(item.Name, printerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Printer queue '{printerName}' was not found.");
        if (!queue.DriverName.Contains("QL-800", StringComparison.OrdinalIgnoreCase) ||
            !queue.PortName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing status probe: '{printerName}' is not a USB Brother QL-800 queue.");
        }

        if (queue.Jobs != 0)
        {
            throw new InvalidOperationException(
                $"Refusing status probe because the queue already contains {queue.Jobs} job(s).");
        }

        string directory = GetOption(args, "--output") ?? CreateRunDirectory("raw-status-probe");
        Directory.CreateDirectory(directory);
        string submissionPath = Path.GetFullPath(Path.Combine(directory, "submission.json"));
        string resultPath = Path.GetFullPath(Path.Combine(directory, "read-result.json"));
        string decodedPath = Path.GetFullPath(Path.Combine(directory, "decoded-status.json"));

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current CLI executable.");
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("probe-status-worker");
        startInfo.ArgumentList.Add("--printer");
        startInfo.ArgumentList.Add(printerName);
        startInfo.ArgumentList.Add("--submission");
        startInfo.ArgumentList.Add(submissionPath);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultPath);

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the isolated status-probe process.");
        Task<string> standardOutput = worker.StandardOutput.ReadToEndAsync();
        Task<string> standardError = worker.StandardError.ReadToEndAsync();

        bool completed = worker.WaitForExit(timeoutMilliseconds);
        if (!completed)
        {
            worker.Kill(entireProcessTree: true);
            worker.WaitForExit();
        }

        string workerOutput = standardOutput.GetAwaiter().GetResult();
        string workerError = standardError.GetAwaiter().GetResult();
        RawStatusSubmission? submission = File.Exists(submissionPath)
            ? JsonSerializer.Deserialize<RawStatusSubmission>(File.ReadAllText(submissionPath), JsonOptions)
            : null;
        RawStatusReadResult? result = File.Exists(resultPath)
            ? JsonSerializer.Deserialize<RawStatusReadResult>(File.ReadAllText(resultPath), JsonOptions)
            : null;

        object? decoded = null;
        if (result is not null && TryFindStatusPacket(result.ResponseHex, out byte[] packet))
        {
            QlPrinterStatus status = new QlStatusDecoder().Decode(packet);
            MediaIdentification media = MediaCatalog.LoadBuiltIn().Identify(status.Media);
            decoded = new { status, media };
            WriteJson(decodedPath, decoded);
        }

        Thread.Sleep(500);
        PrinterQueueInfo? after = discovery.EnumerateQueues().FirstOrDefault(
            item => string.Equals(item.Name, printerName, StringComparison.OrdinalIgnoreCase));
        StatusProbeSummary summary = new(
            DateTimeOffset.UtcNow,
            printerName,
            completed,
            completed ? worker.ExitCode : null,
            timeoutMilliseconds,
            submission,
            result,
            after?.Jobs,
            after?.Status,
            workerOutput,
            workerError,
            decoded is not null);
        WriteJson(Path.Combine(directory, "summary.json"), summary);

        Console.WriteLine($"Printer: {printerName}");
        Console.WriteLine("Request: ESC i S (1B6953), status only");
        Console.WriteLine($"Worker completed: {completed}");
        Console.WriteLine($"Spooler job ID: {submission?.SpoolerJobId.ToString() ?? "not recorded"}");
        if (result is not null)
        {
            Console.WriteLine($"ReadPrinter: {(result.ReadSucceeded ? "success" : $"failed, Win32 error {result.Win32Error}")}");
            Console.WriteLine($"Bytes read: {result.BytesRead}");
        }
        else
        {
            Console.WriteLine(completed
                ? "No read result was produced."
                : $"ReadPrinter did not return within {timeoutMilliseconds} ms; the isolated worker was terminated.");
        }

        Console.WriteLine($"Decoded Brother status: {(decoded is null ? "unavailable" : "available")}");
        Console.WriteLine($"Queue jobs after probe: {after?.Jobs.ToString() ?? "unknown"}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(directory)}");
        Console.WriteLine("No raster data or print command was sent; no media should feed or cut.");
        return submission is null ? 1 : 0;
    }

    private static int RunStatusProbeWorker(string[] args)
    {
        string printerName = RequireOption(args, "--printer");
        string submissionPath = RequireOption(args, "--submission");
        string resultPath = RequireOption(args, "--result");
        WindowsRawStatusProbe probe = new();
        RawStatusReadResult result = probe.Probe(
            printerName,
            submission => WriteJson(submissionPath, submission));
        WriteJson(resultPath, result);
        return 0;
    }

    private static bool TryFindStatusPacket(string responseHex, out byte[] packet)
    {
        packet = [];
        if (string.IsNullOrWhiteSpace(responseHex))
        {
            return false;
        }

        byte[] bytes = Convert.FromHexString(responseHex);
        for (int offset = 0; offset <= bytes.Length - QlStatusDecoder.PacketLength; offset++)
        {
            if (bytes[offset] == 0x80 && bytes[offset + 1] == 0x20)
            {
                packet = bytes[offset..(offset + QlStatusDecoder.PacketLength)];
                return true;
            }
        }

        return false;
    }

    private static int PrintRawPhysicalTest(string[] args, PhysicalTestKind kind)
    {
        const string confirmationFlag = "--confirm-physical-print";
        if (!HasFlag(args, confirmationFlag))
        {
            throw new ArgumentException(
                $"The physical RAW canary requires {confirmationFlag}.");
        }

        string printerName = RequireOption(args, "--printer");
        WindowsPrinterDiscovery discovery = new();
        PrinterQueueInfo queue = discovery.EnumerateQueues().FirstOrDefault(
                item => string.Equals(item.Name, printerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Printer queue '{printerName}' was not found.");
        if (!queue.DriverName.Contains("QL-800", StringComparison.OrdinalIgnoreCase) ||
            !queue.PortName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing RAW canary: '{printerName}' is not a USB Brother QL-800 queue.");
        }

        if (queue.Jobs != 0)
        {
            throw new InvalidOperationException(
                $"Refusing RAW canary because the queue already contains {queue.Jobs} job(s).");
        }

        string directory = GetOption(args, "--output") ?? CreateRunDirectory("raw-canary-dk22251");
        Directory.CreateDirectory(directory);
        bool colourTest = kind == PhysicalTestKind.ColourPlanes;
        bool dieCutTest = kind == PhysicalTestKind.DieCut;
        MediaProfile media = MediaCatalog.LoadBuiltIn().GetRequired(
            dieCutTest ? "brother.dk-11204" : "brother.dk-22251");
        TestPatternGenerator generator = new();
        string testId;
        MonochromeRaster blackRaster;
        MonochromeRaster? redRaster;
        int feedMarginDots;
        int? requestedFinishedLengthMicrometres;
        string lengthPlanningNote;
        if (colourTest)
        {
            GeneratedColourTestPattern colourPattern = generator.GenerateColourPlaneTest(media);
            testId = colourPattern.TestId;
            blackRaster = colourPattern.BlackRaster;
            redRaster = colourPattern.RedRaster;
            feedMarginDots = colourPattern.FeedMarginDots;
            requestedFinishedLengthMicrometres = colourPattern.RequestedFinishedLengthMicrometres;
            lengthPlanningNote = colourPattern.LengthPlanningNote;
        }
        else if (dieCutTest)
        {
            GeneratedTestPattern pattern = generator.Generate("diecut-placement", media);
            testId = pattern.TestId;
            blackRaster = pattern.Raster;
            redRaster = null;
            feedMarginDots = pattern.FeedMarginDots;
            requestedFinishedLengthMicrometres = pattern.RequestedFinishedLengthMicrometres;
            lengthPlanningNote = pattern.LengthPlanningNote;
        }
        else
        {
            string requestedTest = GetOption(args, "--test") ?? "canary-30";
            string[] allowedTests = ["canary-30", "continuous-60", "continuous-100"];
            if (!allowedTests.Contains(requestedTest, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Physical RAW test '{requestedTest}' is not allowed. Allowed tests: {string.Join(", ", allowedTests)}.");
            }

            GeneratedTestPattern pattern = generator.Generate(requestedTest, media);
            testId = pattern.TestId;
            blackRaster = pattern.Raster;
            redRaster = null;
            feedMarginDots = pattern.FeedMarginDots;
            requestedFinishedLengthMicrometres = pattern.RequestedFinishedLengthMicrometres;
            lengthPlanningNote = pattern.LengthPlanningNote;
        }

        QlRasterJob job = new QlRasterJobEncoder().Encode(
            new QlRasterJobOptions(media, blackRaster, feedMarginDots, redRaster));
        QlRasterValidationResult validation = new QlRasterJobValidator().Validate(job.Payload, media);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "Refusing RAW canary because dry validation failed: " + string.Join(" | ", validation.Errors));
        }

        string rasterPath = Path.GetFullPath(Path.Combine(directory, "source-raster.pbm"));
        using (FileStream rasterOutput = File.Create(rasterPath))
        {
            RasterArtifactWriter.WritePortableBitmap(blackRaster, rasterOutput);
        }

        if (redRaster is not null)
        {
            using FileStream redOutput = File.Create(Path.Combine(directory, "red-raster.pbm"));
            RasterArtifactWriter.WritePortableBitmap(redRaster, redOutput);
        }

        string payloadPath = Path.GetFullPath(Path.Combine(directory, "ql-raster-job.bin"));
        File.WriteAllBytes(payloadPath, job.Payload);
        RawCanaryPreparation preparation = new(
            DateTimeOffset.UtcNow,
            printerName,
            queue.PortName,
            testId,
            media.ProfileId,
            media.Sku,
            blackRaster.Width,
            blackRaster.Height,
            feedMarginDots,
            requestedFinishedLengthMicrometres,
            lengthPlanningNote,
            job.Payload.Length,
            Sha256(job.Payload),
            validation,
            colourTest
                ? "One black/red plane diagnostic label is expected to feed and cut. No automatic retry is permitted."
                : dieCutTest
                    ? "One DK-11204 die-cut label is expected to print and feed. No automatic retry is permitted."
                    : "One monochrome label is expected to feed and cut. No automatic retry is permitted.");
        WriteJson(Path.Combine(directory, "preparation.json"), preparation);

        string jobCreatedPath = Path.GetFullPath(Path.Combine(directory, "job-created.json"));
        string submissionPath = Path.GetFullPath(Path.Combine(directory, "submission.json"));
        string workerResultPath = Path.GetFullPath(Path.Combine(directory, "worker-result.json"));
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the current CLI executable.");
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("raw-print-worker");
        startInfo.ArgumentList.Add("--printer");
        startInfo.ArgumentList.Add(printerName);
        startInfo.ArgumentList.Add("--payload");
        startInfo.ArgumentList.Add(payloadPath);
        startInfo.ArgumentList.Add("--document-name");
        startInfo.ArgumentList.Add(colourTest
            ? "QL-800 Spike - RAW Colour Planes"
            : dieCutTest
                ? "QL-800 Spike - DK-11204 Placement"
                : $"QL-800 Spike - RAW {testId}");
        startInfo.ArgumentList.Add("--job-created");
        startInfo.ArgumentList.Add(jobCreatedPath);
        startInfo.ArgumentList.Add("--submission");
        startInfo.ArgumentList.Add(submissionPath);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(workerResultPath);

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the isolated RAW canary process.");
        Task<string> standardOutput = worker.StandardOutput.ReadToEndAsync();
        Task<string> standardError = worker.StandardError.ReadToEndAsync();
        const int workerTimeoutMilliseconds = 30_000;
        bool completed = worker.WaitForExit(workerTimeoutMilliseconds);
        if (!completed)
        {
            worker.Kill(entireProcessTree: true);
            worker.WaitForExit();
        }

        string workerOutput = standardOutput.GetAwaiter().GetResult();
        string workerError = standardError.GetAwaiter().GetResult();
        RawPrintWorkerResult? workerResult = File.Exists(workerResultPath)
            ? JsonSerializer.Deserialize<RawPrintWorkerResult>(File.ReadAllText(workerResultPath), JsonOptions)
            : null;
        uint? createdJobId = File.Exists(jobCreatedPath)
            ? JsonSerializer.Deserialize<RawJobCreated>(File.ReadAllText(jobCreatedPath), JsonOptions)?.JobId
            : null;

        Thread.Sleep(500);
        PrinterQueueInfo? after = discovery.EnumerateQueues().FirstOrDefault(
            item => string.Equals(item.Name, printerName, StringComparison.OrdinalIgnoreCase));
        RawCanarySummary summary = new(
            DateTimeOffset.UtcNow,
            completed,
            completed ? worker.ExitCode : null,
            createdJobId,
            workerResult,
            after?.Jobs,
            after?.Status,
            workerOutput,
            workerError);
        WriteJson(Path.Combine(directory, "summary.json"), summary);

        Console.WriteLine($"Printer: {printerName}");
        Console.WriteLine($"Test: {testId} on DK-22251, cut after label");
        Console.WriteLine($"Payload validation: PASS ({validation.RasterLineCount} rows, {job.Payload.Length} bytes)");
        Console.WriteLine($"Worker completed: {completed}");
        Console.WriteLine($"Spooler job ID: {createdJobId?.ToString() ?? "not recorded"}");
        Console.WriteLine($"Queue jobs after worker: {after?.Jobs.ToString() ?? "unknown"}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(directory)}");
        Console.WriteLine("The spike will not retry this job, regardless of outcome.");
        return completed && worker.ExitCode == 0 ? 0 : 1;
    }

    private static int RunRawPrintWorker(string[] args)
    {
        string printerName = RequireOption(args, "--printer");
        string payloadPath = RequireOption(args, "--payload");
        string documentName = RequireOption(args, "--document-name");
        string jobCreatedPath = RequireOption(args, "--job-created");
        string submissionPath = RequireOption(args, "--submission");
        string resultPath = RequireOption(args, "--result");
        byte[] payload = File.ReadAllBytes(payloadPath);

        WindowsRawSpoolTransport transport = new();
        RawSpoolSubmission submission = transport.Submit(
            printerName,
            documentName,
            payload,
            jobId => WriteJson(jobCreatedPath, new RawJobCreated(DateTimeOffset.UtcNow, jobId)));
        WriteJson(submissionPath, submission);

        PrintJobTimeline timeline = new WindowsPrintJobMonitor().Monitor(
            printerName,
            submission.SpoolerJobId,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(100));
        WriteJson(resultPath, new RawPrintWorkerResult(submission, timeline));
        return 0;
    }

    private static void PrintDiscoverySummary(
        PrinterDiscoveryReport report,
        IReadOnlyList<CapabilityResult> capabilities)
    {
        Console.WriteLine($"Queues found: {report.Queues.Count}");
        foreach (PrinterQueueInfo queue in report.Queues)
        {
            Console.WriteLine($"- {queue.Name}");
            Console.WriteLine($"  Driver: {queue.DriverName} ({queue.Driver?.FileVersion ?? "version unavailable"})");
            Console.WriteLine($"  Port: {queue.PortName}");
            Console.WriteLine($"  Status: {queue.Status}");

            PrinterDeviceCorrelation? correlation = report.Correlations.FirstOrDefault(
                item => string.Equals(item.QueueName, queue.Name, StringComparison.OrdinalIgnoreCase));
            if (correlation is not null)
            {
                Console.WriteLine($"  PnP correlation: {correlation.Confidence}");
                Console.WriteLine($"  Device: {correlation.DeviceInstanceId ?? "unresolved"}");
            }

            CapabilityResult? capability = capabilities.FirstOrDefault(
                item => string.Equals(item.QueueName, queue.Name, StringComparison.OrdinalIgnoreCase));
            if (capability?.Capabilities is not null)
            {
                string resolutions = capability.Capabilities.Resolutions.Count == 0
                    ? "not reported"
                    : string.Join(", ", capability.Capabilities.Resolutions.Select(value => $"{value.DpiX}x{value.DpiY}"));
                Console.WriteLine($"  Resolutions: {resolutions}");
                Console.WriteLine($"  Driver-ready media: " +
                    (capability.Capabilities.DriverReportedReadyMedia.Count == 0
                        ? "not reported"
                        : string.Join(", ", capability.Capabilities.DriverReportedReadyMedia)));
            }
        }

        Console.WriteLine($"PnP printer devices found: {report.PnpDevices.Count}");
    }

    private static string CreateRunDirectory(string suffix)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", $"{timestamp}-{suffix}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void EnsureParentDirectory(string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        EnsureParentDirectory(path);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private static string RequireOption(string[] args, string name) =>
        GetOption(args, name) ?? throw new ArgumentException($"Required option '{name}' was not provided.");

    private static string? GetOption(string[] args, string name)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }

            return args[index + 1];
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsHelp(string value) => value is "help" or "--help" or "-h";

    private static bool IsBrotherCandidate(PrinterQueueInfo queue) =>
        queue.Name.Contains("Brother", StringComparison.OrdinalIgnoreCase) ||
        queue.DriverName.Contains("Brother", StringComparison.OrdinalIgnoreCase) ||
        queue.Name.Contains("QL-800", StringComparison.OrdinalIgnoreCase) ||
        queue.DriverName.Contains("QL-800", StringComparison.OrdinalIgnoreCase);

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 2;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("QL-800 Milestone 0A hardware spike (dry stage)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  environment [--output <file>]");
        Console.WriteLine("  discover [--output <directory>]");
        Console.WriteLine("  status --printer <exact queue name>");
        Console.WriteLine("  list-media");
        Console.WriteLine("  generate --test <id> [--media <profile>] [--output <directory>]");
        Console.WriteLine("  decode-status --hex <64 hex characters>");
        Console.WriteLine("  probe-status --printer <queue> --confirm-nonprinting-status-request");
        Console.WriteLine("  print-raw-canary --printer <queue> [--test canary-30|continuous-60|continuous-100] --confirm-physical-print");
        Console.WriteLine("  print-raw-colour-test --printer <queue> --confirm-physical-print");
        Console.WriteLine("  print-raw-diecut-test --printer <queue> --confirm-physical-print");
        Console.WriteLine();
        Console.WriteLine("Available tests:");
        foreach (string testId in new TestPatternGenerator().TestIds)
        {
            Console.WriteLine($"  {testId}");
        }
        Console.WriteLine();
        Console.WriteLine("No command in this build submits print data.");
    }

    private sealed record CapabilityResult(
        string QueueName,
        PrinterCapabilities? Capabilities,
        string? Error);

    private sealed record DryGenerationManifest(
        DateTimeOffset GeneratedAtUtc,
        string TestId,
        string MediaProfileId,
        string MediaSku,
        int RasterWidthDots,
        int RasterHeightDots,
        int DpiX,
        int DpiY,
        int FeedMarginDots,
        int? RequestedFinishedLengthMicrometres,
        string LengthPlanningNote,
        int PayloadBytes,
        string RasterSha256,
        string PayloadSha256,
        QlRasterValidationResult Validation);

    private sealed record StatusProbeSummary(
        DateTimeOffset CompletedAtUtc,
        string PrinterName,
        bool WorkerCompleted,
        int? WorkerExitCode,
        int TimeoutMilliseconds,
        RawStatusSubmission? Submission,
        RawStatusReadResult? ReadResult,
        uint? QueueJobsAfterProbe,
        WindowsPrinterStatus? QueueStatusAfterProbe,
        string WorkerStandardOutput,
        string WorkerStandardError,
        bool StatusDecoded);

    private sealed record RawCanaryPreparation(
        DateTimeOffset PreparedAtUtc,
        string PrinterName,
        string PortName,
        string TestId,
        string MediaProfileId,
        string MediaSku,
        int RasterWidthDots,
        int RasterHeightDots,
        int FeedMarginDots,
        int? RequestedFinishedLengthMicrometres,
        string LengthPlanningNote,
        int PayloadBytes,
        string PayloadSha256,
        QlRasterValidationResult Validation,
        string SafetyNote);

    private sealed record RawJobCreated(DateTimeOffset CreatedAtUtc, uint JobId);

    private sealed record RawPrintWorkerResult(
        RawSpoolSubmission Submission,
        PrintJobTimeline Timeline);

    private sealed record RawCanarySummary(
        DateTimeOffset CompletedAtUtc,
        bool WorkerCompleted,
        int? WorkerExitCode,
        uint? SpoolerJobId,
        RawPrintWorkerResult? WorkerResult,
        uint? QueueJobsAfterWorker,
        WindowsPrinterStatus? QueueStatusAfterWorker,
        string WorkerStandardOutput,
        string WorkerStandardError);

    private enum PhysicalTestKind
    {
        Continuous,
        ColourPlanes,
        DieCut,
    }
}
