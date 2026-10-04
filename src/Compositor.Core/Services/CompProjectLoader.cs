using System.Text.Json;
using Compositor.Core.Models;

namespace Compositor.Core.Services;

public sealed class CompProjectLoader
{
    public const string ExpectedFormat = "com.compositor.project";
    public const int SupportedManifestVersion = 11;
    public const int MaxCanvasSide = 30_000;
    public const int MaxLayerCount = 10_000;
    public const long MaxManifestBytes = 4L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public async Task<CompProject> LoadAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new ArgumentException("Project path cannot be empty.", nameof(packagePath));
        }

        packagePath = Path.GetFullPath(packagePath);
        if (!Directory.Exists(packagePath))
        {
            throw new DirectoryNotFoundException(packagePath);
        }

        if (!packagePath.EndsWith(".comp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A Compositor project package must end in .comp.");
        }

        var manifestPath = Path.Combine(packagePath, "manifest.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("manifest.json was not found in the .comp package.", manifestPath);
        }

        if (info.Length > MaxManifestBytes)
        {
            throw new InvalidDataException($"manifest.json exceeds the {MaxManifestBytes:N0}-byte safety limit.");
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CompManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("manifest.json could not be deserialized.");

        var project = new CompProject(packagePath, manifest);
        Validate(project);
        return project;
    }

    public void Validate(CompProject project)
    {
        var manifest = project.Manifest;

        if (!string.Equals(manifest.Format, ExpectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported project format '{manifest.Format}'.");
        }

        if (manifest.Version < 1 || manifest.Version > SupportedManifestVersion)
        {
            throw new InvalidDataException(
                $"Unsupported .comp version {manifest.Version}. This build supports versions 1-{SupportedManifestVersion}.");
        }

        if (!Guid.TryParse(manifest.DocumentId, out _))
        {
            throw new InvalidDataException("documentID must be a UUID.");
        }

        if (manifest.Width is <= 0 or > MaxCanvasSide || manifest.Height is <= 0 or > MaxCanvasSide)
        {
            throw new InvalidDataException($"Canvas must be between 1 and {MaxCanvasSide:N0} pixels per side.");
        }

        if (!double.IsFinite(manifest.Resolution) || manifest.Resolution is < 1 or > 9600)
        {
            throw new InvalidDataException("Document resolution must be a finite value from 1 to 9600 ppi.");
        }

        if (manifest.Layers.Count > MaxLayerCount)
        {
            throw new InvalidDataException($"Project exceeds the {MaxLayerCount:N0}-layer safety limit.");
        }

        var byId = new Dictionary<string, CompLayer>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in manifest.Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Id))
            {
                throw new InvalidDataException("Every layer must have an ID.");
            }

            if (!Guid.TryParse(layer.Id, out _))
            {
                throw new InvalidDataException($"Layer ID '{layer.Id}' is not a UUID.");
            }

            if (!byId.TryAdd(layer.Id, layer))
            {
                throw new InvalidDataException($"Duplicate layer ID '{layer.Id}'.");
            }

            if (!double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
            {
                throw new InvalidDataException($"Layer '{layer.Name}' has invalid opacity {layer.Opacity}.");
            }

            ValidateTransform(layer);

            if (layer.IsGroup)
            {
                if (!string.IsNullOrWhiteSpace(layer.ImageFile))
                {
                    throw new InvalidDataException($"Group '{layer.Name}' must not carry imageFile metadata.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(layer.ImageFile))
            {
                var expectedImageName = $"{layer.Id}.png";
                if (!string.Equals(layer.ImageFile, expectedImageName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' image must be named '{expectedImageName}'.");
                }

                var imagePath = project.ResolveImagePath(layer.ImageFile);
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"Layer image '{layer.ImageFile}' is missing.", imagePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(layer.MaskFile))
            {
                var expectedMaskName = $"{layer.Id}.mask.png";
                if (!string.Equals(layer.MaskFile, expectedMaskName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' mask must be named '{expectedMaskName}'.");
                }

                var maskPath = project.ResolveImagePath(layer.MaskFile);
                if (!File.Exists(maskPath))
                {
                    throw new FileNotFoundException($"Layer mask '{layer.MaskFile}' is missing.", maskPath);
                }
            }
        }

        foreach (var layer in manifest.Layers)
        {
            if (!string.IsNullOrWhiteSpace(layer.ParentId))
            {
                if (!byId.TryGetValue(layer.ParentId, out var parent))
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' references missing parent '{layer.ParentId}'.");
                }

                if (!parent.IsGroup)
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' references non-group parent '{parent.Name}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(layer.MaskSourceId))
            {
                if (string.Equals(layer.MaskSourceId, layer.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' cannot use itself as a clipping-mask source.");
                }

                if (!byId.TryGetValue(layer.MaskSourceId, out var source) || source.IsGroup)
                {
                    throw new InvalidDataException($"Layer '{layer.Name}' has an invalid clipping-mask source '{layer.MaskSourceId}'.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.ActiveLayerId) && !byId.ContainsKey(manifest.ActiveLayerId))
        {
            throw new InvalidDataException($"activeLayerID '{manifest.ActiveLayerId}' does not exist.");
        }

        ValidateNoCycles(manifest.Layers, byId);
        ValidateNoMaskSourceCycles(manifest.Layers, byId);
    }

    private static void ValidateTransform(CompLayer layer)
    {
        var t = layer.Transform;
        if (t.Origin.Length != 2 || t.Size.Length != 2)
        {
            throw new InvalidDataException($"Layer '{layer.Name}' transform must have two-value origin and size arrays.");
        }

        if (t.Origin.Any(v => !double.IsFinite(v)) || t.Size.Any(v => !double.IsFinite(v)) || !double.IsFinite(t.Rotation))
        {
            throw new InvalidDataException($"Layer '{layer.Name}' transform contains a non-finite number.");
        }

        if (!layer.IsGroup && !string.IsNullOrWhiteSpace(layer.ImageFile) && (t.Width <= 0 || t.Height <= 0))
        {
            throw new InvalidDataException($"Raster layer '{layer.Name}' must have a positive transform size.");
        }
    }

    private static void ValidateNoCycles(
        IReadOnlyList<CompLayer> layers,
        IReadOnlyDictionary<string, CompLayer> byId)
    {
        foreach (var start in layers)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cursor = start;
            var depth = 0;

            while (!string.IsNullOrWhiteSpace(cursor.ParentId))
            {
                if (!seen.Add(cursor.Id))
                {
                    throw new InvalidDataException($"Layer hierarchy cycle detected at '{cursor.Name}'.");
                }

                if (++depth > 64)
                {
                    throw new InvalidDataException($"Layer hierarchy exceeds 64 levels at '{start.Name}'.");
                }

                cursor = byId[cursor.ParentId];
            }
        }
    }

    private static void ValidateNoMaskSourceCycles(
        IReadOnlyList<CompLayer> layers,
        IReadOnlyDictionary<string, CompLayer> byId)
    {
        foreach (var start in layers)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cursor = start;
            var depth = 0;

            while (!string.IsNullOrWhiteSpace(cursor.MaskSourceId))
            {
                if (!seen.Add(cursor.Id))
                {
                    throw new InvalidDataException($"Clipping-mask cycle detected at '{cursor.Name}'.");
                }

                if (++depth > 256)
                {
                    throw new InvalidDataException($"Clipping-mask chain exceeds 256 links at '{start.Name}'.");
                }

                cursor = byId[cursor.MaskSourceId];
            }
        }
    }
}
