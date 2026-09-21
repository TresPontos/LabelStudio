using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed record ElementMetadata(string? Name, bool IsVisible, bool IsLocked)
{
    public static ElementMetadata From(DocumentElement element) =>
        new(element.Name, element.IsVisible, element.IsLocked);
}

public sealed record ElementMetadataChange(
    string ElementId,
    ElementMetadata Before,
    ElementMetadata After);

public sealed class ChangeElementMetadataCommand : IEditorCommand
{
    private readonly ElementMetadataChange[] _changes;

    public ChangeElementMetadataCommand(ElementMetadataChange change)
        : this([change])
    {
    }

    public ChangeElementMetadataCommand(IEnumerable<ElementMetadataChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = changes.ToArray();
        if (_changes.Select(change => change.ElementId).Distinct(StringComparer.Ordinal).Count() != _changes.Length)
        {
            throw new ArgumentException("An element's metadata can only be changed once.", nameof(changes));
        }
    }

    public string Description => $"Change metadata on {_changes.Length} element(s)";

    public LabelDocument Execute(LabelDocument document) => Apply(document, useAfter: true);

    public LabelDocument Undo(LabelDocument document) => Apply(document, useAfter: false);

    private LabelDocument Apply(LabelDocument document, bool useAfter)
    {
        Dictionary<string, ElementMetadata> metadata = _changes.ToDictionary(
            change => change.ElementId,
            change => useAfter ? change.After : change.Before,
            StringComparer.Ordinal);
        ReplaceElementsCommand.EnsureUniqueTargets(document, metadata.Keys);

        List<DocumentElement> elements = document.Elements.Select(element =>
        {
            if (!metadata.TryGetValue(element.Id, out ElementMetadata? value))
            {
                return element;
            }

            return element with
            {
                Name = value.Name,
                IsVisible = value.IsVisible,
                IsLocked = value.IsLocked,
            };
        }).ToList();
        return document.WithElements(elements.AsReadOnly());
    }
}
