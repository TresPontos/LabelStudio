using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Layout;

public enum PreparedContentType
{
    Rectangle,
    Line,
    Image,
    Text,
}

public sealed record PreparedContent(PreparedContentType ContentType, string? AssetId, string? TextContent)
{
    public static PreparedContent Rectangle() => new(PreparedContentType.Rectangle, null, null);
    public static PreparedContent Line() => new(PreparedContentType.Line, null, null);
    public static PreparedContent Image(string assetId) => new(PreparedContentType.Image, assetId, null);
    public static PreparedContent Text(string text) => new(PreparedContentType.Text, null, text);
}