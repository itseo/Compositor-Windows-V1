using Compositor.Core.Models;

namespace Compositor.Core.Services;

public sealed class EditorSession
{
    public CompProject? Project { get; private set; }

    public string? SelectedLayerId { get; private set; }

    public EditorHistory History { get; } = new();

    public bool HasProject => Project is not null;

    public void Install(CompProject project)
    {
        Project = project;
        SelectedLayerId = project.Manifest.ActiveLayerId;
        if (SelectedLayerId is null || project.Manifest.Layers.All(x => !SameId(x.Id, SelectedLayerId)))
        {
            SelectedLayerId = project.Manifest.Layers.LastOrDefault()?.Id;
        }

        project.Manifest.ActiveLayerId = SelectedLayerId;
        History.Reset();
    }

    public CompProject CreateNew(string packagePath, int width, int height, double resolution = 72)
    {
        if (width is <= 0 or > CompProjectLoader.MaxCanvasSide ||
            height is <= 0 or > CompProjectLoader.MaxCanvasSide)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions are outside the supported range.");
        }

        if (!double.IsFinite(resolution) || resolution is < 1 or > 9600)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Directory.CreateDirectory(packagePath);
        Directory.CreateDirectory(Path.Combine(packagePath, "images"));

        var manifest = new CompManifest
        {
            Format = CompProjectLoader.ExpectedFormat,
            Version = CompProjectLoader.SupportedManifestVersion,
            ColorSpace = "sRGB",
            DocumentId = Guid.NewGuid().ToString().ToUpperInvariant(),
            Width = width,
            Height = height,
            Resolution = resolution,
            ActiveLayerId = null,
            Layers = []
        };

        var project = new CompProject(packagePath, manifest);
        Install(project);
        return project;
    }

    public CompLayer? SelectedLayer
        => Project?.Manifest.Layers.FirstOrDefault(x => SameId(x.Id, SelectedLayerId));

    public void Select(string? layerId)
    {
        if (Project is null)
        {
            SelectedLayerId = null;
            return;
        }

        if (layerId is not null && Project.Manifest.Layers.All(x => !SameId(x.Id, layerId)))
        {
            return;
        }

        SelectedLayerId = layerId;
        Project.Manifest.ActiveLayerId = layerId;
    }

    public void AddLayer(CompLayer layer, string historyName = "Add Layer")
    {
        Mutate(historyName, manifest =>
        {
            if (manifest.Layers.Count >= CompProjectLoader.MaxLayerCount)
            {
                throw new InvalidOperationException("The document has reached the layer limit.");
            }

            if (manifest.Layers.Any(x => SameId(x.Id, layer.Id)))
            {
                throw new InvalidOperationException("A layer with this ID already exists.");
            }

            manifest.Layers.Add(layer);
            Select(layer.Id);
        });
    }

    public CompLayer AddGroup(string name = "Group")
    {
        EnsureProject();
        var group = new CompLayer
        {
            Id = Guid.NewGuid().ToString().ToUpperInvariant(),
            Name = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim(),
            IsGroup = true,
            IsVisible = true,
            Opacity = 1,
            BlendMode = "Normal",
            Transform = new CompTransform
            {
                Origin = [0, 0],
                Size = [Project!.Manifest.Width, Project.Manifest.Height],
                Sampling = "High quality"
            }
        };

        AddLayer(group, "Add Group");
        return group;
    }

    public void RenameSelected(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Layer name cannot be empty.", nameof(name));
        }

        Mutate("Rename Layer", _ => SelectedLayer!.Name = trimmed);
    }

    public void ToggleSelectedVisibility()
        => Mutate("Toggle Layer Visibility", _ => SelectedLayer!.IsVisible = !SelectedLayer!.IsVisible);

    public void SetSelectedOpacity(double opacity)
        => Mutate("Change Layer Opacity", _ => SelectedLayer!.Opacity = Math.Clamp(opacity, 0, 1));

    public void CropCanvas(double x, double y, double width, double height)
    {
        EnsureProject();

        if (![x, y, width, height].All(double.IsFinite) ||
            width < 1 ||
            height < 1)
        {
            throw new ArgumentException("Crop bounds must be finite and at least one pixel.");
        }

        var manifest = Project!.Manifest;
        var left = Math.Clamp(Math.Floor(x), 0, manifest.Width - 1);
        var top = Math.Clamp(Math.Floor(y), 0, manifest.Height - 1);
        var right = Math.Clamp(Math.Ceiling(x + width), left + 1, manifest.Width);
        var bottom = Math.Clamp(Math.Ceiling(y + height), top + 1, manifest.Height);

        Mutate("Crop Canvas", document =>
        {
            document.Width = checked((int)(right - left));
            document.Height = checked((int)(bottom - top));

            foreach (var layer in document.Layers)
            {
                layer.Transform.Origin =
                [
                    layer.Transform.X - left,
                    layer.Transform.Y - top
                ];
            }
        });
    }

    public void NudgeSelected(double deltaX, double deltaY)
    {
        var layer = SelectedLayer;
        if (layer is null || layer.IsGroup)
        {
            return;
        }

        Mutate("Nudge Layer", _ =>
        {
            layer.Transform.Origin =
            [
                layer.Transform.X + deltaX,
                layer.Transform.Y + deltaY
            ];
        });
    }

    public void ToggleSelectedFlip(bool horizontal)
    {
        var layer = SelectedLayer;
        if (layer is null || layer.IsGroup)
        {
            return;
        }

        Mutate(horizontal ? "Flip Layer Horizontally" : "Flip Layer Vertically", _ =>
        {
            if (horizontal)
            {
                layer.Transform.FlipX = !layer.Transform.FlipX;
            }
            else
            {
                layer.Transform.FlipY = !layer.Transform.FlipY;
            }
        });
    }

    public void SetSelectedTransform(double x, double y, double width, double height, double rotation)
    {
        if (!new[] { x, y, width, height, rotation }.All(double.IsFinite))
        {
            throw new ArgumentException("Transform values must be finite numbers.");
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        Mutate("Transform Layer", _ =>
        {
            var layer = SelectedLayer!;
            layer.Transform.Origin = [x, y];
            layer.Transform.Size = [width, height];
            layer.Transform.Rotation = rotation;
        });
    }

    public void DeleteSelected()
    {
        EnsureSelection();
        var selectedId = SelectedLayerId!;

        Mutate("Delete Layer", manifest =>
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectedId };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var layer in manifest.Layers)
                {
                    if (layer.ParentId is not null && ids.Contains(layer.ParentId) && ids.Add(layer.Id))
                    {
                        changed = true;
                    }
                }
            }

            manifest.Layers.RemoveAll(x => ids.Contains(x.Id));
            var next = manifest.Layers.LastOrDefault()?.Id;
            SelectedLayerId = next;
            manifest.ActiveLayerId = next;
        });
    }

    public void MoveSelectedInStack(int direction)
    {
        EnsureSelection();
        if (direction == 0)
        {
            return;
        }

        Mutate(direction > 0 ? "Move Layer Up" : "Move Layer Down", manifest =>
        {
            var selected = SelectedLayer!;
            var siblings = manifest.Layers
                .Where(x => SameId(x.ParentId, selected.ParentId))
                .ToList();

            var siblingIndex = siblings.FindIndex(x => SameId(x.Id, selected.Id));
            var targetSiblingIndex = siblingIndex + Math.Sign(direction);
            if (siblingIndex < 0 || targetSiblingIndex < 0 || targetSiblingIndex >= siblings.Count)
            {
                return;
            }

            var other = siblings[targetSiblingIndex];
            var a = manifest.Layers.FindIndex(x => SameId(x.Id, selected.Id));
            var b = manifest.Layers.FindIndex(x => SameId(x.Id, other.Id));
            (manifest.Layers[a], manifest.Layers[b]) = (manifest.Layers[b], manifest.Layers[a]);
        });
    }

    public CompLayer DuplicateSelected()
    {
        EnsureSelection();
        var selectedId = SelectedLayerId!;
        CompLayer? duplicatedRoot = null;

        Mutate("Duplicate Layer", manifest =>
        {
            var selected = SelectedLayer!;
            var originals = manifest.Layers
                .Where(layer => SameId(layer.Id, selectedId) || IsDescendantOf(layer, selectedId, manifest.Layers))
                .ToList();

            var idMap = originals.ToDictionary(
                x => x.Id,
                _ => Guid.NewGuid().ToString().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase);

            var copies = new List<CompLayer>(originals.Count);
            foreach (var original in originals)
            {
                var copy = ManifestCloner.CloneLayer(original);
                var oldId = original.Id;
                copy.Id = idMap[oldId];

                if (SameId(oldId, selectedId))
                {
                    copy.Name = original.Name + " copy";
                }

                if (copy.ParentId is not null && idMap.TryGetValue(copy.ParentId, out var mappedParent))
                {
                    copy.ParentId = mappedParent;
                }

                if (copy.MaskSourceId is not null && idMap.TryGetValue(copy.MaskSourceId, out var mappedSource))
                {
                    copy.MaskSourceId = mappedSource;
                }

                copy.Transform.Origin =
                [
                    copy.Transform.X + 20,
                    copy.Transform.Y + 20
                ];

                if (!string.IsNullOrWhiteSpace(original.ImageFile))
                {
                    var newName = $"{copy.Id}.png";
                    File.Copy(
                        Project!.ResolveImagePath(original.ImageFile),
                        Project.ResolveImagePath(newName),
                        overwrite: true);
                    copy.ImageFile = newName;
                }

                if (!string.IsNullOrWhiteSpace(original.MaskFile))
                {
                    var newName = $"{copy.Id}.mask.png";
                    File.Copy(
                        Project!.ResolveImagePath(original.MaskFile),
                        Project.ResolveImagePath(newName),
                        overwrite: true);
                    copy.MaskFile = newName;
                }

                copies.Add(copy);
                if (SameId(oldId, selectedId))
                {
                    duplicatedRoot = copy;
                }
            }

            manifest.Layers.AddRange(copies);
            SelectedLayerId = duplicatedRoot?.Id;
            manifest.ActiveLayerId = SelectedLayerId;
        });

        return duplicatedRoot ?? throw new InvalidOperationException("The selected layer could not be duplicated.");
    }

    public void RecordExternalEdit(
        string name,
        CompManifest before,
        string? beforeSelection)
    {
        EnsureProject();
        History.Record(
            name,
            before,
            beforeSelection,
            Project!.Manifest,
            SelectedLayerId);
    }

    public bool Undo()
    {
        EnsureProject();
        if (!History.CanUndo)
        {
            return false;
        }

        SelectedLayerId = History.Undo(Project!);
        Project!.Manifest.ActiveLayerId = SelectedLayerId;
        return true;
    }

    public bool Redo()
    {
        EnsureProject();
        if (!History.CanRedo)
        {
            return false;
        }

        SelectedLayerId = History.Redo(Project!);
        Project!.Manifest.ActiveLayerId = SelectedLayerId;
        return true;
    }

    private void Mutate(string name, Action<CompManifest> action)
    {
        EnsureProject();
        var before = ManifestCloner.Clone(Project!.Manifest);
        var beforeSelection = SelectedLayerId;

        action(Project.Manifest);

        if (SelectedLayerId is not null &&
            Project.Manifest.Layers.All(x => !SameId(x.Id, SelectedLayerId)))
        {
            SelectedLayerId = Project.Manifest.Layers.LastOrDefault()?.Id;
        }

        Project.Manifest.ActiveLayerId = SelectedLayerId;
        History.Record(name, before, beforeSelection, Project.Manifest, SelectedLayerId);
    }

    private void EnsureProject()
    {
        if (Project is null)
        {
            throw new InvalidOperationException("No document is open.");
        }
    }

    private void EnsureSelection()
    {
        EnsureProject();
        if (SelectedLayer is null)
        {
            throw new InvalidOperationException("No layer is selected.");
        }
    }

    private static bool IsDescendantOf(CompLayer layer, string ancestorId, IReadOnlyList<CompLayer> layers)
    {
        var byId = layers.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var parentId = layer.ParentId;
        var depth = 0;

        while (parentId is not null && depth++ < 64)
        {
            if (SameId(parentId, ancestorId))
            {
                return true;
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                return false;
            }

            parentId = parent.ParentId;
        }

        return false;
    }

    private static bool SameId(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
