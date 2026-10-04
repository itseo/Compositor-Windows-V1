using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Core.Models;

namespace Compositor.Core.Services;

public sealed class CompProjectStore
{
    private readonly CompProjectLoader _loader = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task SaveAsync(
        CompProject project,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty.", nameof(destinationPath));
        }

        destinationPath = Path.GetFullPath(destinationPath);
        if (!destinationPath.EndsWith(".comp", StringComparison.OrdinalIgnoreCase))
        {
            destinationPath += ".comp";
        }

        _loader.Validate(project);

        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The destination must have a parent directory.");
        Directory.CreateDirectory(parent);

        var stagingPath = destinationPath + ".saving-" + Guid.NewGuid().ToString("N");
        var backupPath = destinationPath + ".backup-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(stagingPath);
            var stageImages = Path.Combine(stagingPath, "images");
            Directory.CreateDirectory(stageImages);

            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in project.Manifest.Layers)
            {
                foreach (var fileName in new[] { layer.ImageFile, layer.MaskFile }.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!copied.Add(fileName!))
                    {
                        continue;
                    }

                    var source = project.ResolveImagePath(fileName!);
                    if (!File.Exists(source))
                    {
                        throw new FileNotFoundException($"Project asset '{fileName}' is missing.", source);
                    }

                    File.Copy(source, Path.Combine(stageImages, fileName!), overwrite: true);
                }
            }

            var manifestPath = Path.Combine(stagingPath, "manifest.json");
            await using (var stream = File.Create(manifestPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    project.Manifest,
                    JsonOptions,
                    cancellationToken);
            }

            var manifestLength = new FileInfo(manifestPath).Length;
            if (manifestLength > CompProjectLoader.MaxManifestBytes)
            {
                throw new InvalidDataException("The saved manifest exceeds the project safety limit.");
            }

            var destinationExists = Directory.Exists(destinationPath);
            if (destinationExists)
            {
                Directory.Move(destinationPath, backupPath);
            }

            try
            {
                Directory.Move(stagingPath, destinationPath);
            }
            catch
            {
                if (destinationExists && Directory.Exists(backupPath) && !Directory.Exists(destinationPath))
                {
                    Directory.Move(backupPath, destinationPath);
                }

                throw;
            }

            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }

            project.PackagePath = destinationPath;
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            if (Directory.Exists(backupPath) && Directory.Exists(destinationPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }
        }
    }
}
