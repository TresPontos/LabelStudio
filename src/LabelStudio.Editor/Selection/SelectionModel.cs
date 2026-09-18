using LabelStudio.Document;

namespace LabelStudio.Editor.Selection;

public sealed class SelectionModel
{
    private readonly HashSet<string> _selectedIds = new();
    private string? _activeId;

    public IReadOnlyCollection<string> SelectedIds => _selectedIds;
    public string? ActiveId => _activeId;
    public bool IsMultiSelect => _selectedIds.Count > 1;
    public bool HasSelection => _selectedIds.Count > 0;
    public int Count => _selectedIds.Count;

    public event EventHandler? SelectionChanged;

    public void Select(string elementId)
    {
        _selectedIds.Clear();
        _selectedIds.Add(elementId);
        _activeId = elementId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSelect(string elementId)
    {
        if (_selectedIds.Contains(elementId))
        {
            _selectedIds.Remove(elementId);
            if (_activeId == elementId)
            {
                _activeId = _selectedIds.LastOrDefault();
            }
        }
        else
        {
            _selectedIds.Add(elementId);
            _activeId = elementId;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(IEnumerable<string> ids)
    {
        _selectedIds.Clear();
        foreach (string id in ids)
        {
            _selectedIds.Add(id);
        }
        _activeId = _selectedIds.LastOrDefault();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _selectedIds.Clear();
        _activeId = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Contains(string elementId) => _selectedIds.Contains(elementId);

    public void PruneDeleted(LabelDocument document)
    {
        HashSet<string> existing = document.Elements.Select(e => e.Id).ToHashSet();
        if (_selectedIds.Except(existing).Any())
        {
            _selectedIds.RemoveWhere(id => !existing.Contains(id));
            if (_activeId is not null && !existing.Contains(_activeId))
            {
                _activeId = _selectedIds.LastOrDefault();
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}