using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Geometry;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor;

public static class GroupGeometry
{
    public static MicrometreRect GetGroupBounds(LabelDocument document, string groupId)
    {
        ElementGroup? group = document.DesignMetadata.Groups
            .FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
        if (group is null || group.MemberIds.Count == 0)
        {
            return MicrometreRect.Zero;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (string memberId in group.MemberIds)
        {
            DocumentElement? element = document.Elements.FirstOrDefault(e => string.Equals(e.Id, memberId, StringComparison.Ordinal));
            if (element is null) continue;

            GeometryRect visualBounds = ElementGeometry.GetVisualBounds(element.Bounds, element.RotationMillidegrees);
            minX = Math.Min(minX, visualBounds.X);
            minY = Math.Min(minY, visualBounds.Y);
            maxX = Math.Max(maxX, visualBounds.Right);
            maxY = Math.Max(maxY, visualBounds.Bottom);
        }

        if (minX == double.MaxValue)
        {
            return MicrometreRect.Zero;
        }

        return ElementGeometry.RoundBounds(new GeometryRect(minX, minY, maxX - minX, maxY - minY));
    }
}