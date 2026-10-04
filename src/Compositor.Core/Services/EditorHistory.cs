using Compositor.Core.Models;

namespace Compositor.Core.Services;

public sealed class EditorHistory
{
    private sealed record Entry(
        string Name,
        CompManifest Before,
        string? BeforeSelection,
        CompManifest After,
        string? AfterSelection);

    private readonly List<Entry> _past = [];
    private readonly List<Entry> _future = [];
    private readonly int _limit;

    public EditorHistory(int limit = 100)
    {
        _limit = Math.Max(0, limit);
    }

    public bool CanUndo => _past.Count > 0;
    public bool CanRedo => _future.Count > 0;
    public string UndoName => _past.LastOrDefault()?.Name ?? string.Empty;
    public string RedoName => _future.LastOrDefault()?.Name ?? string.Empty;

    public void Reset()
    {
        _past.Clear();
        _future.Clear();
    }

    public void Record(
        string name,
        CompManifest before,
        string? beforeSelection,
        CompManifest after,
        string? afterSelection)
    {
        if (ManifestCloner.Equivalent(before, after))
        {
            return;
        }

        _past.Add(new Entry(
            name,
            ManifestCloner.Clone(before),
            beforeSelection,
            ManifestCloner.Clone(after),
            afterSelection));
        _future.Clear();

        while (_past.Count > _limit)
        {
            _past.RemoveAt(0);
        }
    }

    public string? Undo(CompProject project)
    {
        if (_past.Count == 0)
        {
            return null;
        }

        var entry = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        _future.Add(entry);
        project.Manifest = ManifestCloner.Clone(entry.Before);
        return entry.BeforeSelection;
    }

    public string? Redo(CompProject project)
    {
        if (_future.Count == 0)
        {
            return null;
        }

        var entry = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        _past.Add(entry);
        project.Manifest = ManifestCloner.Clone(entry.After);
        return entry.AfterSelection;
    }
}
