using Compositor.Core.Models;
using Compositor.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Compositor.Windows.Services;

internal sealed record ImportedRaster(string LayerId, string Name, uint PixelWidth, uint PixelHeight);

internal sealed class ImageImportService
{
    public static readonly string[] SupportedExtensions =
    [
        ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".heic"
    ];

    public async Task<ImportedRaster> ImportAsync(
        StorageFile source,
        CompProject project,
        string? layerId = null)
    {
        layerId ??= Guid.NewGuid().ToString().ToUpperInvariant();

        using var input = await source.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(input);

        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        if (width == 0 || height == 0 ||
            width > CompProjectLoader.MaxCanvasSide ||
            height > CompProjectLoader.MaxCanvasSide)
        {
            throw new InvalidDataException("The image dimensions are outside Compositor's supported range.");
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        Directory.CreateDirectory(project.ImagesPath);
        var imagesFolder = await StorageFolder.GetFolderFromPathAsync(project.ImagesPath);
        var output = await imagesFolder.CreateFileAsync(
            $"{layerId}.png",
            CreationCollisionOption.ReplaceExisting);

        using var outputStream = await output.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        return new ImportedRaster(
            layerId,
            Path.GetFileNameWithoutExtension(source.Name),
            width,
            height);
    }
}
