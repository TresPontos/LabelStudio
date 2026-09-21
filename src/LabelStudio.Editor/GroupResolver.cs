using LabelStudio.Document;

namespace LabelStudio.Editor;

public static class GroupResolver
{
    public static string? FindGroupIdForMember(this LabelDocument document, string elementId)
    {
        ElementGroup? group = document.DesignMetadata.Groups
            .FirstOrDefault(g => g.MemberIds.Contains(elementId, StringComparer.Ordinal));
        return group?.Id;
    }

    public static bool IsGroupMember(this LabelDocument document, string elementId)
    {
        return document.DesignMetadata.Groups
            .Any(g => g.MemberIds.Contains(elementId, StringComparer.Ordinal));
    }
}