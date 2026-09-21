using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor;

public sealed class DragTransaction
{
    private readonly Dictionary<string, MicrometreRect> _beforeBounds = new();
    private readonly Dictionary<string, MicrometreRect> _currentBounds = new();
    private readonly Dictionary<string, DocumentElement> _beforeElements = new();
    private bool _active;

    public bool IsActive => _active;
    public IReadOnlyDictionary<string, MicrometreRect> CurrentBounds => _currentBounds;

    public void Begin(IReadOnlyCollection<string> elementIds, LabelDocument document)
    {
        _beforeBounds.Clear();
        _currentBounds.Clear();
        _beforeElements.Clear();

        foreach (string id in elementIds)
        {
            Document.Elements.DocumentElement? element = document.Elements.FirstOrDefault(e => e.Id == id);
            if (element is not null)
            {
                _beforeBounds[id] = element.Bounds;
                _currentBounds[id] = element.Bounds;
                _beforeElements[id] = element;
            }
        }

        _active = true;
    }

    public void UpdatePreview(string elementId, MicrometreRect newBounds)
    {
        if (!_active) return;
        _currentBounds[elementId] = newBounds;
    }

    public IEditorCommand? Commit(LabelDocument document)
    {
        if (!_active) return null;
        _active = false;

        var replacements = new List<(DocumentElement Before, DocumentElement After)>();
        foreach (KeyValuePair<string, MicrometreRect> entry in _currentBounds)
        {
            if (_beforeBounds.TryGetValue(entry.Key, out MicrometreRect oldBounds) && oldBounds != entry.Value)
            {
                DocumentElement before = _beforeElements[entry.Key];
                replacements.Add((before, ElementFactory.WithBounds(before, entry.Value)));
            }
        }

        _beforeBounds.Clear();
        _currentBounds.Clear();
        _beforeElements.Clear();

        return replacements.Count > 0 ? new ReplaceElementsCommand(replacements) : null;
    }

    public void Cancel()
    {
        _active = false;
        _beforeBounds.Clear();
        _currentBounds.Clear();
        _beforeElements.Clear();
    }
}
