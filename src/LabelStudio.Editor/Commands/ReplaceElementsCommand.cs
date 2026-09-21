using LabelStudio.Document;
using LabelStudio.Document.Elements;

namespace LabelStudio.Editor.Commands;

public sealed class ReplaceElementsCommand : IEditorCommand
{
    private readonly Replacement[] _replacements;

    public ReplaceElementsCommand(
        IReadOnlyDictionary<string, (DocumentElement Before, DocumentElement After)> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        _replacements = replacements.Select(pair =>
        {
            if (pair.Key != pair.Value.Before.Id || pair.Key != pair.Value.After.Id)
            {
                throw new ArgumentException("Replacement IDs must match their dictionary key.", nameof(replacements));
            }

            return new Replacement(pair.Key, pair.Value.Before, pair.Value.After);
        }).ToArray();
    }

    public ReplaceElementsCommand(
        IReadOnlyList<(DocumentElement Before, DocumentElement After)> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        _replacements = replacements.Select(pair =>
        {
            if (pair.Before.Id != pair.After.Id)
            {
                throw new ArgumentException("Before and after element IDs must match.", nameof(replacements));
            }

            return new Replacement(pair.Before.Id, pair.Before, pair.After);
        }).ToArray();

        if (_replacements.Select(replacement => replacement.Id).Distinct().Count() != _replacements.Length)
        {
            throw new ArgumentException("An element can only be replaced once.", nameof(replacements));
        }
    }

    public string Description => $"Replace {_replacements.Length} element(s)";

    public LabelDocument Execute(LabelDocument document) => Apply(document, useAfter: true);

    public LabelDocument Undo(LabelDocument document) => Apply(document, useAfter: false);

    private LabelDocument Apply(LabelDocument document, bool useAfter)
    {
        Dictionary<string, Replacement> byId = _replacements.ToDictionary(replacement => replacement.Id);
        EnsureUniqueTargets(document, byId.Keys);

        List<DocumentElement> elements = document.Elements.Select(element =>
        {
            if (!byId.TryGetValue(element.Id, out Replacement? replacement))
            {
                return element;
            }

            return useAfter ? replacement.After : replacement.Before;
        }).ToList();
        return document.WithElements(elements.AsReadOnly());
    }

    internal static void EnsureUniqueTargets(LabelDocument document, IEnumerable<string> targetIds)
    {
        HashSet<string> targets = targetIds.ToHashSet(StringComparer.Ordinal);
        string? duplicate = document.Elements
            .Where(element => targets.Contains(element.Id))
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Document contains duplicate element ID '{duplicate}'.");
        }
    }

    private sealed record Replacement(string Id, DocumentElement Before, DocumentElement After);
}
