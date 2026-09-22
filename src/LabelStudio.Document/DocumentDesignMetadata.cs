using LabelStudio.Document.Units;

namespace LabelStudio.Document;

public sealed record DocumentDesignMetadata
{
    public DocumentDesignMetadata(
        IEnumerable<ElementGroup> groups,
        IEnumerable<DocumentGuide> guides,
        DocumentGridGeometry grid,
        DocumentSafeMargins? safeMargins = null)
    {
        Groups = groups.ToList().AsReadOnly();
        Guides = guides.ToList().AsReadOnly();
        Grid = grid;
        SafeMargins = safeMargins ?? DocumentSafeMargins.Default;
    }

    public IReadOnlyList<ElementGroup> Groups { get; }
    public IReadOnlyList<DocumentGuide> Guides { get; }
    public DocumentGridGeometry Grid { get; }
    public DocumentSafeMargins SafeMargins { get; }

    public static DocumentDesignMetadata Default { get; } = new(
        Array.Empty<ElementGroup>(),
        Array.Empty<DocumentGuide>(),
        DocumentGridGeometry.Default,
        DocumentSafeMargins.Default);

    public static DocumentDesignMetadata CreateDefault(PhysicalSize pageDimensions) => new(
        Array.Empty<ElementGroup>(),
        Array.Empty<DocumentGuide>(),
        DocumentGridGeometry.Default,
        DocumentSafeMargins.Default);
}

public sealed record ElementGroup
{
    public ElementGroup(
        string id,
        string? name,
        IEnumerable<string> memberIds,
        bool isVisible = true,
        bool isLocked = false)
    {
        Id = id;
        Name = name;
        MemberIds = memberIds.ToList().AsReadOnly();
        IsVisible = isVisible;
        IsLocked = isLocked;
    }

    public string Id { get; }
    public string? Name { get; }
    public IReadOnlyList<string> MemberIds { get; }
    public bool IsVisible { get; }
    public bool IsLocked { get; }
}

public enum DocumentGuideOrientation
{
    Horizontal,
    Vertical,
}

public sealed record DocumentGuide(
    string Id,
    DocumentGuideOrientation Orientation,
    Micrometre Position,
    string? Name = null);

public sealed record DocumentGridGeometry(
    Micrometre XSpacing,
    Micrometre YSpacing,
    MicrometrePoint Origin,
    int MajorInterval)
{
    public static DocumentGridGeometry Default { get; } = new(
        Micrometre.FromMillimetres(1),
        Micrometre.FromMillimetres(1),
        MicrometrePoint.Zero,
        10);
}
