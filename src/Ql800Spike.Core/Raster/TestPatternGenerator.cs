using Ql800Spike.Core.Geometry;
using Ql800Spike.Core.Media;

namespace Ql800Spike.Core.Raster;

public sealed class TestPatternGenerator
{
    public const int StandardDpi = 300;
    public const int HeadWidthDots = 720;

    public IReadOnlyList<string> TestIds =>
    [
        "canary-30",
        "geometry-60",
        "border-60",
        "pixel-30",
        "continuous-30",
        "continuous-60",
        "continuous-100",
        "diecut-placement",
    ];

    public GeneratedTestPattern Generate(string testId, MediaProfile media)
    {
        return testId.ToLowerInvariant() switch
        {
            "canary-30" => GenerateContinuous(media, testId, 30_000, DrawCanary),
            "geometry-60" => GenerateContinuous(media, testId, 60_000, DrawGeometry),
            "border-60" => GenerateContinuous(media, testId, 60_000, DrawBorder),
            "pixel-30" => GenerateContinuous(media, testId, 30_000, DrawPixelPattern),
            "continuous-30" => GenerateContinuous(media, testId, 30_000, DrawLengthPattern),
            "continuous-60" => GenerateContinuous(media, testId, 60_000, DrawLengthPattern),
            "continuous-100" => GenerateContinuous(media, testId, 100_000, DrawLengthPattern),
            "diecut-placement" => GenerateDieCut(media, testId),
            _ => throw new ArgumentException($"Unknown test pattern '{testId}'.", nameof(testId)),
        };
    }

    public GeneratedColourTestPattern GenerateColourPlaneTest(MediaProfile media)
    {
        if (media.Kind != MediaKind.Continuous || !media.SupportedInks.Contains(ThermalInk.Red))
        {
            throw new ArgumentException("The colour-plane test requires continuous black/red media.", nameof(media));
        }

        const int requestedLengthMicrometres = 30_000;
        int totalLengthDots = PhysicalUnits.MicrometresToDots(requestedLengthMicrometres, StandardDpi);
        int feedMarginDots = media.BrotherQl.MinimumFeedMarginDots;
        int rasterLines = totalLengthDots - (2 * feedMarginDots);
        MonochromeRaster black = new(HeadWidthDots, rasterLines);
        MonochromeRaster red = new(HeadWidthDots, rasterLines);
        (int left, int width) = GetPrintableHorizontal(media);

        black.DrawRectangle(left + 60, 50, 140, 100, 4);
        PixelFont.DrawText(black, left + 94, 180, "BLACK", 2);
        black.DrawHorizontalLine(left + 40, 25, 180, 4);

        red.DrawRectangle(left + width - 200, 50, 140, 100, 4);
        PixelFont.DrawText(red, left + width - 170, 180, "RED", 2);
        red.DrawHorizontalLine(left + width - 220, rasterLines - 29, 180, 4);

        return new GeneratedColourTestPattern(
            "colour-planes-30",
            media,
            black,
            red,
            feedMarginDots,
            requestedLengthMicrometres,
            "Colour-plane diagnostic with equal leading and trailing feed margins included in the requested finished length.");
    }

    private static GeneratedTestPattern GenerateContinuous(
        MediaProfile media,
        string testId,
        int requestedLengthMicrometres,
        Action<MonochromeRaster, MediaProfile, string> draw)
    {
        if (media.Kind != MediaKind.Continuous)
        {
            throw new ArgumentException($"Test '{testId}' requires continuous media.", nameof(media));
        }

        int totalLengthDots = PhysicalUnits.MicrometresToDots(requestedLengthMicrometres, StandardDpi);
        int feedMarginDots = media.BrotherQl.MinimumFeedMarginDots;
        int rasterLines = totalLengthDots - (2 * feedMarginDots);
        if (rasterLines <= 0)
        {
            throw new InvalidOperationException("Requested length is shorter than the required feed margin.");
        }

        MonochromeRaster raster = new(HeadWidthDots, rasterLines);
        draw(raster, media, testId);

        return new GeneratedTestPattern(
            testId,
            media,
            raster,
            feedMarginDots,
            requestedLengthMicrometres,
            "Finished continuous length equals raster rows plus equal leading and trailing feed margins.");
    }

    private static GeneratedTestPattern GenerateDieCut(MediaProfile media, string testId)
    {
        if (media.Kind != MediaKind.DieCut)
        {
            throw new ArgumentException($"Test '{testId}' requires die-cut media.", nameof(media));
        }

        int height = media.BrotherQl.PrintableLengthDots
            ?? throw new InvalidOperationException("Die-cut media is missing its fixed printable length.");

        MonochromeRaster raster = new(HeadWidthDots, height);
        DrawDieCutPlacement(raster, media, testId);

        return new GeneratedTestPattern(
            testId,
            media,
            raster,
            0,
            media.PhysicalLengthMicrometres,
            "Die-cut raster rows use Brother's fixed printable-length table; feed margin command is zero.");
    }

    private static void DrawCanary(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        int right = left + width - 1;

        raster.DrawVerticalLine(left + 12, 12, Math.Min(120, raster.Height - 24), 5);
        raster.DrawHorizontalLine(left + 12, 12, Math.Min(160, width - 24), 5);
        raster.FillRectangle(right - 24, raster.Height - 24, 16, 16);
        PixelFont.DrawText(raster, left + 36, 36, "CANARY-30", 2);

        int tenMillimetres = PhysicalUnits.MicrometresToDots(10_000, StandardDpi);
        raster.DrawHorizontalLine(left + ((width - tenMillimetres) / 2), raster.Height - 40, tenMillimetres, 2);
    }

    private static void DrawGeometry(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        int horizontalLength = PhysicalUnits.MicrometresToDots(50_000, StandardDpi);
        int verticalLength = PhysicalUnits.MicrometresToDots(40_000, StandardDpi);
        int squareSize = PhysicalUnits.MicrometresToDots(10_000, StandardDpi);

        int horizontalX = left + ((width - horizontalLength) / 2);
        raster.DrawHorizontalLine(horizontalX, 60, horizontalLength, 2);

        int verticalX = left + (width / 2);
        int verticalY = (raster.Height - verticalLength) / 2;
        raster.DrawVerticalLine(verticalX, verticalY, verticalLength, 2);

        int squareX = left + width - squareSize - 24;
        int squareY = raster.Height - squareSize - 24;
        raster.DrawRectangle(squareX, squareY, squareSize, squareSize, 2);
        PixelFont.DrawText(raster, left + 24, 20, "GEO-60", 2);
    }

    private static void DrawBorder(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        int maxInset = Math.Min(24, Math.Min(width / 4, raster.Height / 4));

        foreach (int inset in new[] { 0, 3, 6, 12, 24 }.Where(value => value <= maxInset))
        {
            raster.DrawRectangle(left + inset, inset, width - (2 * inset), raster.Height - (2 * inset));
        }

        raster.DrawVerticalLine(left + (width / 2), 0, raster.Height);
        PixelFont.DrawText(raster, left + 36, 36, "BORDER-60", 2);
    }

    private static void DrawPixelPattern(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        PixelFont.DrawText(raster, left + 24, 20, "PIXEL-30", 2);

        int y = 70;
        for (int thickness = 1; thickness <= 4; thickness *= 2)
        {
            raster.DrawHorizontalLine(left + 24, y, Math.Min(300, width - 48), thickness);
            raster.DrawVerticalLine(left + 340, y, Math.Min(80, raster.Height - y - 1), thickness);
            y += 28;
        }

        int checkerTop = Math.Min(y + 20, raster.Height - 40);
        for (int row = 0; row < Math.Min(32, raster.Height - checkerTop); row++)
        {
            for (int column = 0; column < Math.Min(128, width - 48); column++)
            {
                if (((row + column) & 1) == 0)
                {
                    raster.SetPixel(left + 24 + column, checkerTop + row);
                }
            }
        }
    }

    private static void DrawLengthPattern(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        string label = testId.Replace("continuous", "CONT", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        PixelFont.DrawText(raster, left + 24, 20, label, 2);
        raster.DrawHorizontalLine(left + 12, 12, width - 24, 2);
        raster.DrawHorizontalLine(left + 12, raster.Height - 14, width - 24, 2);

        int tenMillimetres = PhysicalUnits.MicrometresToDots(10_000, StandardDpi);
        for (int y = 24; y < raster.Height - 24; y += tenMillimetres)
        {
            raster.DrawHorizontalLine(left + width - 50, y, 38, 2);
        }
    }

    private static void DrawDieCutPlacement(MonochromeRaster raster, MediaProfile media, string testId)
    {
        (int left, int width) = GetPrintableHorizontal(media);
        raster.DrawRectangle(left, 0, width, raster.Height);

        int centreX = left + (width / 2);
        int centreY = raster.Height / 2;
        raster.DrawVerticalLine(centreX, 0, raster.Height);
        raster.DrawHorizontalLine(left, centreY, width);

        int tenMillimetres = Math.Min(
            PhysicalUnits.MicrometresToDots(10_000, StandardDpi),
            Math.Min(width - 24, raster.Height - 24));
        raster.DrawRectangle(
            centreX - (tenMillimetres / 2),
            centreY - (tenMillimetres / 2),
            tenMillimetres,
            tenMillimetres,
            2);
        PixelFont.DrawText(raster, left + 12, 20, "DIECUT", 1);
    }

    private static (int Left, int Width) GetPrintableHorizontal(MediaProfile media) =>
        (media.BrotherQl.HeadLeftBlankDots, media.BrotherQl.PrintableWidthDots);
}
