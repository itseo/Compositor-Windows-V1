namespace Compositor.Core.Models;

public sealed record CompProject(string PackagePath, CompManifest Manifest)
{
    public string ImagesPath => Path.Combine(PackagePath, "images");

    public string ResolveImagePath(string imageFile)
    {
        if (string.IsNullOrWhiteSpace(imageFile))
        {
            throw new ArgumentException("Image file cannot be empty.", nameof(imageFile));
        }

        if (!string.Equals(Path.GetFileName(imageFile), imageFile, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsafe image path '{imageFile}'.");
        }

        var imagesRoot = Path.GetFullPath(ImagesPath) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(ImagesPath, imageFile));

        if (!candidate.StartsWith(imagesRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Image path escapes the package: '{imageFile}'.");
        }

        return candidate;
    }
}
