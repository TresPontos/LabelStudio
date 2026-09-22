using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor.Transforms;

public sealed class ElementTransform
{
    public string ElementId { get; }
    public DocumentElement Before { get; }
    public DocumentElement After { get; }

    public ElementTransform(string elementId, DocumentElement before, DocumentElement after)
    {
        ElementId = elementId;
        Before = before;
        After = after;
    }
}

public sealed class SelectionBounds
{
    public static MicrometreRect GetCombinedBounds(LabelDocument document, IEnumerable<string> elementIds)
    {
        HashSet<string> ids = elementIds.ToHashSet(StringComparer.Ordinal);
        int? minX = null, minY = null, maxX = null, maxY = null;

        foreach (DocumentElement element in document.Elements)
        {
            if (!ids.Contains(element.Id)) continue;

            int left = element.Bounds.X.Value;
            int top = element.Bounds.Y.Value;
            int right = element.Bounds.Right.Value;
            int bottom = element.Bounds.Bottom.Value;

            minX = Math.Min(minX ?? left, left);
            minY = Math.Min(minY ?? top, top);
            maxX = Math.Max(maxX ?? right, right);
            maxY = Math.Max(maxY ?? bottom, bottom);
        }

        if (minX is null || minY is null || maxX is null || maxY is null)
        {
            return MicrometreRect.Zero;
        }

        return new MicrometreRect(
            new(minX.Value), new(minY.Value),
            new(maxX.Value - minX.Value), new(maxY.Value - minY.Value));
    }

    public static IReadOnlyList<string> ResolveTransformableElementIds(LabelDocument document, SelectionModel selection)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (SelectionTarget target in selection.Targets)
        {
            switch (target)
            {
                case SelectionTarget.ElementTarget elem:
                    if (document.Elements.Any(e => e.Id == elem.ElementId))
                    {
                        ids.Add(elem.ElementId);
                    }
                    break;
                case SelectionTarget.GroupTarget grp:
                    ElementGroup? group = document.DesignMetadata.Groups
                        .FirstOrDefault(g => string.Equals(g.Id, grp.GroupId, StringComparison.Ordinal));
                    if (group is not null)
                    {
                        foreach (string memberId in group.MemberIds)
                        {
                            if (document.Elements.Any(e => e.Id == memberId))
                            {
                                ids.Add(memberId);
                            }
                        }
                    }
                    break;
            }
        }
        return ids.ToList().AsReadOnly();
    }

    public static bool ContainsLockedMember(LabelDocument document, IEnumerable<string> elementIds)
    {
        foreach (string id in elementIds)
        {
            if (document.IsEffectivelyLocked(id))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class SelectionTransformService
{
    public const int MinElementWidthMicrometres = 100;
    public const int MinElementHeightMicrometres = 100;

    public static ElementTransform[] PlanMove(
        LabelDocument document,
        IReadOnlyCollection<string> elementIds,
        int deltaX,
        int deltaY)
    {
        List<ElementTransform> transforms = new();
        foreach (string id in elementIds)
        {
            DocumentElement? element = document.Elements.FirstOrDefault(e => e.Id == id);
            if (element is null) continue;

            MicrometreRect newBounds = new(
                new(element.Bounds.X.Value + deltaX),
                new(element.Bounds.Y.Value + deltaY),
                element.Bounds.Width,
                element.Bounds.Height);

            transforms.Add(new ElementTransform(id, element, ElementFactory.WithBounds(element, newBounds)));
        }
        return transforms.ToArray();
    }

    public static ElementTransform[] PlanResize(
        LabelDocument document,
        IReadOnlyCollection<string> elementIds,
        MicrometreRect originalBounds,
        MicrometreRect newBounds,
        bool proportional = false)
    {
        double originalWidth = originalBounds.Width.Value;
        double originalHeight = originalBounds.Height.Value;
        double newWidth = newBounds.Width.Value;
        double newHeight = newBounds.Height.Value;

        if (proportional && originalWidth > 0 && originalHeight > 0)
        {
            double growX = newWidth / originalWidth;
            double growY = newHeight / originalHeight;
            double uniform = Math.Max(growX, growY);
            newWidth = Math.Max(MinElementWidthMicrometres, originalWidth * uniform);
            newHeight = Math.Max(MinElementHeightMicrometres, originalHeight * uniform);
        }

        newWidth = Math.Max(MinElementWidthMicrometres, newWidth);
        newHeight = Math.Max(MinElementHeightMicrometres, newHeight);

        double scaleX = originalWidth > 0 ? newWidth / originalWidth : 1.0;
        double scaleY = originalHeight > 0 ? newHeight / originalHeight : 1.0;

        List<ElementTransform> transforms = new();
        foreach (string id in elementIds)
        {
            DocumentElement? element = document.Elements.FirstOrDefault(e => e.Id == id);
            if (element is null) continue;

            MicrometreRect bounds = element.Bounds;
            double relX = originalWidth > 0 ? (bounds.X.Value - originalBounds.X.Value) / originalWidth : 0;
            double relY = originalHeight > 0 ? (bounds.Y.Value - originalBounds.Y.Value) / originalHeight : 0;
            double relW = originalWidth > 0 ? bounds.Width.Value / originalWidth : 1.0;
            double relH = originalHeight > 0 ? bounds.Height.Value / originalHeight : 1.0;

            int newX = newBounds.X.Value + (int)Math.Round(relX * newWidth, MidpointRounding.AwayFromZero);
            int newY = newBounds.Y.Value + (int)Math.Round(relY * newHeight, MidpointRounding.AwayFromZero);
            int newW = Math.Max(MinElementWidthMicrometres, (int)Math.Round(relW * newWidth, MidpointRounding.AwayFromZero));
            int newH = Math.Max(MinElementHeightMicrometres, (int)Math.Round(relH * newHeight, MidpointRounding.AwayFromZero));

            MicrometreRect finalBounds = new(new(newX), new(newY), new(newW), new(newH));
            DocumentElement resized = ElementFactory.WithBounds(element, finalBounds);
            if (resized is TextElement text)
            {
                resized = text with { FrameSizing = TextFrameSizingMode.Fixed };
            }
            transforms.Add(new ElementTransform(id, element, resized));
        }
        return transforms.ToArray();
    }

    public static ElementTransform[] PlanMoveTo(
        LabelDocument document,
        IReadOnlyCollection<string> elementIds,
        int targetX,
        int targetY)
    {
        MicrometreRect combined = SelectionBounds.GetCombinedBounds(document, elementIds);
        return PlanMove(document, elementIds, targetX - combined.X.Value, targetY - combined.Y.Value);
    }

    public static ElementTransform[] PlanScaleToSize(
        LabelDocument document,
        IReadOnlyCollection<string> elementIds,
        int targetWidth,
        int targetHeight)
    {
        MicrometreRect combined = SelectionBounds.GetCombinedBounds(document, elementIds);
        MicrometreRect newBounds = new(combined.X, combined.Y, new(targetWidth), new(targetHeight));
        return PlanResize(document, elementIds, combined, newBounds);
    }

    public static ReplaceElementsCommand ToCommand(IEnumerable<ElementTransform> transforms)
    {
        List<(DocumentElement Before, DocumentElement After)> replacements = transforms
            .Where(t => t.Before.Bounds != t.After.Bounds)
            .Select(t => (t.Before, t.After))
            .ToList();
        return new ReplaceElementsCommand(replacements);
    }
}
