using LabelStudio.Document;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;
using LabelStudio.Layout;

namespace LabelStudio.Printing;

public sealed class PreflightEngine
{
    public PreflightResult Check(LabelDocument document, PreparedScene scene, MediaProfile media, PrintSettings settings)
    {
        List<PreflightIssue> errors = [];
        List<PreflightIssue> warnings = [];
        List<PreflightIssue> information = [];

        CheckPageDimensions(document, media, errors);
        CheckPrintableArea(document, scene, errors, warnings);
        CheckInkCompatibility(document, media, errors);
        CheckResolution(settings, media, errors);
        CheckCutterMode(settings, media, errors);
        CheckElementsInBounds(scene, media, errors, warnings);

        return new PreflightResult(errors, warnings, information);
    }

    private static void CheckPageDimensions(LabelDocument document, MediaProfile media, List<PreflightIssue> errors)
    {
        PhysicalSize dims = document.PageDimensions;

        if (dims.Width <= Micrometre.Zero || dims.Height <= Micrometre.Zero)
        {
            errors.Add(new(PreflightSeverity.Error, "PAGE_BOUNDS_INVALID",
                "Document width and length must be finite and positive."));
        }

        bool documentIsContinuous = document.MediaKind == DocumentMediaKind.Continuous;
        bool profileIsContinuous = media.Kind == MediaKind.Continuous;
        if (documentIsContinuous != profileIsContinuous)
        {
            errors.Add(new(PreflightSeverity.Error, "MEDIA_KIND_MISMATCH",
                $"Document media kind {document.MediaKind} does not match profile media kind {media.Kind}."));
        }

        if (profileIsContinuous)
        {
            if (dims.Width.Value != media.PhysicalWidthMicrometres)
            {
                errors.Add(new(PreflightSeverity.Error, "DIMENSION_MISMATCH",
                    $"Document width {dims.Width} does not match media width {new Micrometre(media.PhysicalWidthMicrometres)}."));
            }

            if (media.Cutter.MinimumContinuousLength is Micrometre minimum && dims.Height < minimum)
            {
                errors.Add(new(PreflightSeverity.Error, "CONTINUOUS_LENGTH",
                    $"Document length {dims.Height} is shorter than the cutter minimum {minimum}."));
            }

            if (media.Cutter.MaximumContinuousLength is Micrometre maximum && dims.Height > maximum)
            {
                errors.Add(new(PreflightSeverity.Error, "CONTINUOUS_LENGTH",
                    $"Document length {dims.Height} exceeds the cutter maximum {maximum}."));
            }
        }
        else
        {
            if (dims.Width.Value != media.PhysicalWidthMicrometres)
            {
                errors.Add(new(PreflightSeverity.Error, "DIMENSION_MISMATCH",
                    $"Document width {dims.Width} does not match media width {new Micrometre(media.PhysicalWidthMicrometres)}."));
            }

            if (media.PhysicalLengthMicrometres is not null &&
                dims.Height.Value != media.PhysicalLengthMicrometres.Value)
            {
                errors.Add(new(PreflightSeverity.Error, "DIMENSION_MISMATCH",
                    $"Document height {dims.Height} does not match die-cut length {new Micrometre(media.PhysicalLengthMicrometres.Value)}."));
            }
        }
    }

    private static void CheckPrintableArea(LabelDocument document, PreparedScene scene, List<PreflightIssue> errors, List<PreflightIssue> warnings)
    {
        MicrometreRect printable = scene.PrintableArea;
        PhysicalSize label = scene.LabelSize;

        if (printable.Width <= Micrometre.Zero || printable.Height < Micrometre.Zero)
        {
            errors.Add(new(PreflightSeverity.Error, "PRINTABLE_AREA_INVALID",
                "Printable area has zero or negative dimensions."));
        }

        if (printable.Right > label.Width)
        {
            warnings.Add(new(PreflightSeverity.Warning, "PRINTABLE_AREA_OVERFLOW",
                $"Printable area extends beyond label width ({printable.Right} > {label.Width})."));
        }

        if (printable.Bottom > label.Height)
        {
            warnings.Add(new(PreflightSeverity.Warning, "PRINTABLE_AREA_OVERFLOW",
                $"Printable area extends beyond label length ({printable.Bottom} > {label.Height})."));
        }
    }

    private static void CheckInkCompatibility(LabelDocument document, MediaProfile media, List<PreflightIssue> errors)
    {
        if (document.PrintDefaults.DefaultInk == InkChannel.Red && !media.SupportsRed)
        {
            errors.Add(new(PreflightSeverity.Error, "INK_UNSUPPORTED",
                $"Media {media.Sku} does not support red ink but document defaults to red."));
        }

        foreach (Document.Elements.DocumentElement element in document.Elements)
        {
            if (!document.IsEffectivelyVisible(element))
            {
                continue;
            }
            if (element.Ink == InkChannel.Red && !media.SupportsRed)
            {
                errors.Add(new(PreflightSeverity.Error, "INK_UNSUPPORTED",
                    $"Element '{element.Id}' uses red ink but media {media.Sku} does not support it.",
                    element.Id));
            }
        }
    }

    private static void CheckResolution(PrintSettings settings, MediaProfile media, List<PreflightIssue> errors)
    {
        // The current render contract has one DPI value for both axes. QL-800's
        // higher mode is asymmetric (300x600), so only 300x300 is representable.
        if (settings.Dpi != 300)
        {
            errors.Add(new(PreflightSeverity.Error, "INVALID_RESOLUTION",
                $"Resolution {settings.Dpi} DPI is unsupported; this QL pipeline supports 300 DPI."));
        }
    }

    private static void CheckCutterMode(PrintSettings settings, MediaProfile media, List<PreflightIssue> errors)
    {
        if (settings.AutoCut && !media.Cutter.SupportsAutomaticCut)
        {
            errors.Add(new(PreflightSeverity.Error, "CUTTER_UNSUPPORTED",
                $"Media {media.Sku} does not support automatic cutting."));
        }

        if (settings.CutAtEnd && !media.Cutter.SupportsAutomaticCut)
        {
            errors.Add(new(PreflightSeverity.Error, "CUTTER_UNSUPPORTED",
                $"Media {media.Sku} does not support cut-at-end."));
        }
    }

    private static void CheckElementsInBounds(PreparedScene scene, MediaProfile media, List<PreflightIssue> errors, List<PreflightIssue> warnings)
    {
        PhysicalSize label = scene.LabelSize;

        foreach (PreparedElement element in scene.Elements)
        {
            MicrometreRect bounds = ElementGeometry.RoundBounds(
                ElementGeometry.GetVisualBounds(element.Bounds, element.RotationMillidegrees));

            if (bounds.X < Micrometre.Zero || bounds.Y < Micrometre.Zero)
            {
                errors.Add(new(PreflightSeverity.Error, "ELEMENT_OUTSIDE_LABEL",
                    $"Element '{element.SourceElementId}' extends above or left of the label origin.",
                    element.SourceElementId));
            }

            if (bounds.Right > label.Width)
            {
                warnings.Add(new(PreflightSeverity.Warning, "ELEMENT_OUTSIDE_LABEL",
                    $"Element '{element.SourceElementId}' extends beyond the label width ({bounds.Right} > {label.Width}).",
                    element.SourceElementId));
            }

            if (bounds.Bottom > label.Height)
            {
                warnings.Add(new(PreflightSeverity.Warning, "ELEMENT_OUTSIDE_LABEL",
                    $"Element '{element.SourceElementId}' extends beyond the label height ({bounds.Bottom} > {label.Height}).",
                    element.SourceElementId));
            }

            MicrometreRect printable = scene.PrintableArea;
            if (printable.Height > Micrometre.Zero &&
                !bounds.Intersects(printable))
            {
                errors.Add(new(PreflightSeverity.Error, "ELEMENT_OUTSIDE_PRINTABLE",
                    $"Element '{element.SourceElementId}' is entirely outside the printable area.",
                    element.SourceElementId));
            }
        }
    }
}
