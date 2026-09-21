using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class BringForwardCommand : ZOrderCommand
{
    public BringForwardCommand(IEnumerable<string> elementIds, LabelDocument document)
        : base(elementIds, document, MoveForward)
    {
    }

    public override string Description => "Bring forward";

    private static IReadOnlyList<string> MoveForward(IReadOnlyList<string> ids, HashSet<string> selected)
    {
        List<string> result = ids.ToList();
        for (int index = result.Count - 2; index >= 0; index--)
        {
            if (selected.Contains(result[index]) && !selected.Contains(result[index + 1]))
            {
                (result[index], result[index + 1]) = (result[index + 1], result[index]);
            }
        }
        return result;
    }
}

public sealed class SendBackwardCommand : ZOrderCommand
{
    public SendBackwardCommand(IEnumerable<string> elementIds, LabelDocument document)
        : base(elementIds, document, MoveBackward)
    {
    }

    public override string Description => "Send backward";

    private static IReadOnlyList<string> MoveBackward(IReadOnlyList<string> ids, HashSet<string> selected)
    {
        List<string> result = ids.ToList();
        for (int index = 1; index < result.Count; index++)
        {
            if (selected.Contains(result[index]) && !selected.Contains(result[index - 1]))
            {
                (result[index], result[index - 1]) = (result[index - 1], result[index]);
            }
        }
        return result;
    }
}

public sealed class BringToFrontCommand : ZOrderCommand
{
    public BringToFrontCommand(IEnumerable<string> elementIds, LabelDocument document)
        : base(elementIds, document, (ids, selected) =>
            [.. ids.Where(id => !selected.Contains(id)), .. ids.Where(selected.Contains)])
    {
    }

    public override string Description => "Bring to front";
}

public sealed class SendToBackCommand : ZOrderCommand
{
    public SendToBackCommand(IEnumerable<string> elementIds, LabelDocument document)
        : base(elementIds, document, (ids, selected) =>
            [.. ids.Where(selected.Contains), .. ids.Where(id => !selected.Contains(id))])
    {
    }

    public override string Description => "Send to back";
}

public abstract class ZOrderCommand : IEditorCommand
{
    private readonly string[] _beforeIds;
    private readonly string[] _afterIds;

    protected ZOrderCommand(
        IEnumerable<string> elementIds,
        LabelDocument document,
        Func<IReadOnlyList<string>, HashSet<string>, IReadOnlyList<string>> reorder)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reorder);

        _beforeIds = document.Elements.Select(element => element.Id).ToArray();
        if (_beforeIds.Distinct(StringComparer.Ordinal).Count() != _beforeIds.Length)
        {
            throw new InvalidOperationException("Document contains duplicate element IDs.");
        }

        HashSet<string> existing = _beforeIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> selected = elementIds.Where(existing.Contains).ToHashSet(StringComparer.Ordinal);
        _afterIds = reorder(_beforeIds, selected).ToArray();
    }

    public abstract string Description { get; }

    public LabelDocument Execute(LabelDocument document) => Apply(document, _afterIds);

    public LabelDocument Undo(LabelDocument document) => Apply(document, _beforeIds);

    private static LabelDocument Apply(LabelDocument document, IReadOnlyList<string> order)
    {
        Dictionary<string, DocumentElement> byId = document.Elements.ToDictionary(
            element => element.Id,
            StringComparer.Ordinal);
        if (byId.Count != order.Count || order.Any(id => !byId.ContainsKey(id)))
        {
            throw new InvalidOperationException("Document elements no longer match the z-order command snapshot.");
        }

        if (document.Elements.Select(element => element.Id).SequenceEqual(order, StringComparer.Ordinal))
        {
            return document;
        }

        return document.WithElements(order.Select(id => byId[id]).ToList().AsReadOnly());
    }
}
