using LabelStudio.Document.Elements;

namespace LabelStudio.Document;

public static class ElementState
{
    public static bool IsEffectivelyVisible(this LabelDocument document, DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        return element.IsVisible && document.DesignMetadata.Groups
            .Where(group => group.MemberIds.Contains(element.Id, StringComparer.Ordinal))
            .All(group => group.IsVisible);
    }

    public static bool IsEffectivelyVisible(this LabelDocument document, string elementId)
    {
        DocumentElement? element = document.Elements.FirstOrDefault(
            candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        return element is not null && document.IsEffectivelyVisible(element);
    }

    public static bool IsEffectivelyLocked(this LabelDocument document, DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        return element.IsLocked || document.DesignMetadata.Groups
            .Where(group => group.MemberIds.Contains(element.Id, StringComparer.Ordinal))
            .Any(group => group.IsLocked);
    }

    public static bool IsEffectivelyLocked(this LabelDocument document, string elementId)
    {
        DocumentElement? element = document.Elements.FirstOrDefault(
            candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        return element is not null && document.IsEffectivelyLocked(element);
    }
}
