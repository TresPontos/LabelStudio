using System.Collections.ObjectModel;
using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Selection;

public sealed class SelectionModel
{
    private readonly Dictionary<string, SelectionTarget> _targets = new(StringComparer.Ordinal);
    private string? _activeId;
    private string? _activeGroupId;

    public IReadOnlyCollection<SelectionTarget> Targets => _targets.Values.ToList().AsReadOnly();
    public IReadOnlyCollection<string> SelectedIds => _targets.Keys.ToList().AsReadOnly();
    public string? ActiveId => _activeId;
    public string? ActiveGroupId => _activeGroupId;
    public bool IsMultiSelect => _targets.Count > 1;
    public bool HasSelection => _targets.Count > 0;
    public int Count => _targets.Count;

    public event EventHandler? SelectionChanged;

    public bool Contains(string id) => _targets.ContainsKey(id);

    public bool ContainsElement(string elementId) =>
        _targets.TryGetValue(elementId, out SelectionTarget? target) && target is SelectionTarget.ElementTarget;

    public bool ContainsGroup(string groupId) =>
        _targets.TryGetValue(groupId, out SelectionTarget? target) && target is SelectionTarget.GroupTarget;

    public bool IsGroupMember(LabelDocument document, string elementId)
    {
        return document.DesignMetadata.Groups
            .Any(group => group.MemberIds.Contains(elementId, StringComparer.Ordinal) &&
                          _targets.ContainsKey(group.Id));
    }

    public IReadOnlyCollection<string> GetSelectedElementIds(LabelDocument document)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (SelectionTarget target in _targets.Values)
        {
            switch (target)
            {
                case SelectionTarget.ElementTarget elem:
                    ids.Add(elem.ElementId);
                    break;
                case SelectionTarget.GroupTarget grp:
                    ElementGroup? group = document.DesignMetadata.Groups
                        .FirstOrDefault(g => string.Equals(g.Id, grp.GroupId, StringComparison.Ordinal));
                    if (group is not null)
                    {
                        foreach (string memberId in group.MemberIds)
                        {
                            ids.Add(memberId);
                        }
                    }
                    break;
            }
        }
        return ids;
    }

    public IReadOnlyCollection<string> GetTransformableElementIds(LabelDocument document)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (SelectionTarget target in _targets.Values)
        {
            switch (target)
            {
                case SelectionTarget.ElementTarget elem:
                    ids.Add(elem.ElementId);
                    break;
                case SelectionTarget.GroupTarget grp:
                    ElementGroup? group = document.DesignMetadata.Groups
                        .FirstOrDefault(g => string.Equals(g.Id, grp.GroupId, StringComparison.Ordinal));
                    if (group is not null)
                    {
                        foreach (string memberId in group.MemberIds)
                        {
                            ids.Add(memberId);
                        }
                    }
                    break;
            }
        }

        foreach (string id in ids)
        {
            if (document.IsEffectivelyLocked(id) || !document.IsEffectivelyVisible(id))
            {
                return new List<string>().AsReadOnly();
            }
        }

        return ids;
    }

    public bool HasLockedMembers(LabelDocument document)
    {
        foreach (SelectionTarget target in _targets.Values)
        {
            if (target is SelectionTarget.GroupTarget grp)
            {
                ElementGroup? group = document.DesignMetadata.Groups
                    .FirstOrDefault(g => string.Equals(g.Id, grp.GroupId, StringComparison.Ordinal));
                if (group is null) continue;
                if (group.IsLocked) return true;
                foreach (string memberId in group.MemberIds)
                {
                    if (document.IsEffectivelyLocked(memberId)) return true;
                }
            }
            else if (target is SelectionTarget.ElementTarget elem)
            {
                if (document.IsEffectivelyLocked(elem.ElementId)) return true;
            }
        }
        return false;
    }

    public void SelectElement(string elementId)
    {
        _targets.Clear();
        _targets[elementId] = SelectionTarget.Element(elementId);
        _activeId = elementId;
        _activeGroupId = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectGroup(string groupId)
    {
        _targets.Clear();
        _targets[groupId] = SelectionTarget.Group(groupId);
        _activeId = null;
        _activeGroupId = groupId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSelectElement(string elementId)
    {
        if (_targets.ContainsKey(elementId))
        {
            _targets.Remove(elementId);
            if (_activeId == elementId)
            {
                _activeId = _targets.Values.OfType<SelectionTarget.ElementTarget>().LastOrDefault()?.ElementId;
            }
        }
        else
        {
            _targets[elementId] = SelectionTarget.Element(elementId);
            _activeId = elementId;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(IEnumerable<SelectionTarget> targets)
    {
        _targets.Clear();
        foreach (SelectionTarget target in targets)
        {
            _targets[target.Id] = target;
        }
        UpdateActiveFromTargets();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetElementSelection(IEnumerable<string> elementIds)
    {
        _targets.Clear();
        foreach (string id in elementIds)
        {
            _targets[id] = SelectionTarget.Element(id);
        }
        UpdateActiveFromTargets();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _targets.Clear();
        _activeId = null;
        _activeGroupId = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PruneDeleted(LabelDocument document)
    {
        HashSet<string> existingElementIds = document.Elements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> existingGroupIds = document.DesignMetadata.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        bool changed = false;

        List<string> toRemove = [];
        foreach (KeyValuePair<string, SelectionTarget> entry in _targets)
        {
            bool exists = entry.Value switch
            {
                SelectionTarget.ElementTarget => existingElementIds.Contains(entry.Key),
                SelectionTarget.GroupTarget => existingGroupIds.Contains(entry.Key),
                _ => false,
            };
            if (!exists)
            {
                toRemove.Add(entry.Key);
            }
        }

        foreach (string id in toRemove)
        {
            _targets.Remove(id);
            changed = true;
        }

        if (_activeId is not null && !existingElementIds.Contains(_activeId))
        {
            _activeId = _targets.Values.OfType<SelectionTarget.ElementTarget>().LastOrDefault()?.ElementId;
            changed = true;
        }

        if (_activeGroupId is not null && !existingGroupIds.Contains(_activeGroupId))
        {
            _activeGroupId = _targets.Values.OfType<SelectionTarget.GroupTarget>().LastOrDefault()?.GroupId;
            changed = true;
        }

        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateActiveFromTargets()
    {
        _activeId = _targets.Values.OfType<SelectionTarget.ElementTarget>().LastOrDefault()?.ElementId;
        _activeGroupId = _targets.Values.OfType<SelectionTarget.GroupTarget>().LastOrDefault()?.GroupId;
    }
}