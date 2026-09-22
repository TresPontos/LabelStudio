using LabelStudio.Document;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class ChangePageLengthCommand : IEditorCommand
{
    private readonly PhysicalSize _before;
    private readonly PhysicalSize _after;

    public ChangePageLengthCommand(LabelDocument document, Micrometre newLength)
        : this(document, new PhysicalSize(document.PageDimensions.Width, newLength))
    {
    }

    public ChangePageLengthCommand(LabelDocument document, PhysicalSize newDimensions)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.MediaKind != DocumentMediaKind.Continuous)
        {
            throw new InvalidOperationException("Page length can only be changed for continuous media.");
        }
        if (newDimensions.Width != document.PageDimensions.Width)
        {
            throw new ArgumentException("Changing page length must not change page width.", nameof(newDimensions));
        }
        if (newDimensions.Height <= Micrometre.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(newDimensions), "Page length must be finite and positive.");
        }

        _before = document.PageDimensions;
        _after = newDimensions;
    }

    public string Description => "Change page length";

    public LabelDocument Execute(LabelDocument document)
    {
        EnsureCompatible(document);
        return Apply(document, _after);
    }

    public LabelDocument Undo(LabelDocument document)
    {
        EnsureCompatible(document);
        return Apply(document, _before);
    }

    private void EnsureCompatible(LabelDocument document)
    {
        if (document.MediaKind != DocumentMediaKind.Continuous ||
            document.PageDimensions.Width != _before.Width)
        {
            throw new InvalidOperationException("The document media kind or width changed after the command was created.");
        }
    }

    private static LabelDocument Apply(LabelDocument document, PhysicalSize dimensions)
    {
        MicrometreRect printable = document.MediaGeometry.PrintableArea;
        int previousPhysicalHeight = document.MediaGeometry.PhysicalDimensions.Height.Value;
        int trailingMargin = previousPhysicalHeight > 0
            ? Math.Max(0, previousPhysicalHeight - printable.Bottom.Value)
            : printable.Y.Value;
        int printableHeight = Math.Max(0, dimensions.Height.Value - printable.Y.Value - trailingMargin);
        MediaSnapshot mediaGeometry = document.MediaGeometry with
        {
            PhysicalDimensions = dimensions,
            PrintableArea = printable with { Height = new Micrometre(printableHeight) },
        };
        return document with { PageDimensions = dimensions, MediaGeometry = mediaGeometry };
    }
}
