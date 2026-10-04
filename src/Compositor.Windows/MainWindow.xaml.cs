using System.Collections.ObjectModel;
using Compositor.Core.Models;
using Compositor.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Compositor.Windows;

public sealed partial class MainWindow : Window
{
    private readonly CompProjectLoader _loader = new();
    private readonly RenderPlanBuilder _renderPlanBuilder = new();
    private readonly ObservableCollection<LayerListItem> _layerItems = [];

    private CompProject? _project;
    private double _zoom = 0.5;
    private bool _isRendering;

    public MainWindow()
    {
        InitializeComponent();
        LayersList.ItemsSource = _layerItems;
        ExtendsContentIntoTitleBar = false;
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            await LoadProjectAsync(folder.Path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not open project", ex.Message);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            await LoadProjectAsync(_project.PackagePath);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not reload project", ex.Message);
        }
    }

    private async Task LoadProjectAsync(string packagePath)
    {
        StatusText.Text = "Loading project…";
        var project = await _loader.LoadAsync(packagePath);
        _project = project;

        DocumentInfo.Text = $"{project.Manifest.Width:N0} × {project.Manifest.Height:N0} · v{project.Manifest.Version} · {project.Manifest.ColorSpace}";
        Title = $"{Path.GetFileName(packagePath)} — Compositor for Windows";
        ReloadButton.IsEnabled = true;
        EmptyState.Visibility = Visibility.Collapsed;

        PopulateLayers(project);
        await RenderProjectAsync();
    }

    private void PopulateLayers(CompProject project)
    {
        _layerItems.Clear();

        // UI panels traditionally show the topmost layer first.
        foreach (var layer in project.Manifest.Layers.AsEnumerable().Reverse())
        {
            var detail = layer.IsGroup
                ? "Folder"
                : string.IsNullOrWhiteSpace(layer.ImageFile)
                    ? "Non-raster layer"
                    : layer.BlendMode;

            _layerItems.Add(new LayerListItem(
                layer.Name,
                layer.IsGroup ? "▾" : "▣",
                detail,
                $"{layer.Opacity:P0}"));
        }
    }

    private async Task RenderProjectAsync()
    {
        if (_project is null || _isRendering)
        {
            return;
        }

        _isRendering = true;
        try
        {
            StatusText.Text = "Rendering…";
            ProjectCanvas.Children.Clear();

            var manifest = _project.Manifest;
            ProjectCanvas.Width = manifest.Width;
            ProjectCanvas.Height = manifest.Height;
            CanvasBorder.Width = manifest.Width;
            CanvasBorder.Height = manifest.Height;
            CanvasSurface.Width = manifest.Width * _zoom;
            CanvasSurface.Height = manifest.Height * _zoom;
            CanvasBorder.RenderTransform = new ScaleTransform { ScaleX = _zoom, ScaleY = _zoom };
            CanvasBorder.RenderTransformOrigin = new global::Windows.Foundation.Point(0, 0);

            var operations = _renderPlanBuilder.Build(_project);
            var unsupportedBlendCount = 0;

            foreach (var operation in operations)
            {
                if (!operation.EffectiveVisible || operation.EffectiveOpacity <= 0)
                {
                    continue;
                }

                if (operation.HasUnsupportedBlendMode)
                {
                    unsupportedBlendCount++;
                }

                var image = await CreateLayerImageAsync(operation);
                ProjectCanvas.Children.Add(image);
            }

            var rendered = ProjectCanvas.Children.Count;
            StatusText.Text = unsupportedBlendCount == 0
                ? $"Rendered {rendered} raster layer(s)"
                : $"Rendered {rendered} raster layer(s); {unsupportedBlendCount} non-Normal blend mode(s) shown as Normal";
        }
        finally
        {
            _isRendering = false;
        }
    }

    private static async Task<Image> CreateLayerImageAsync(RenderOperation operation)
    {
        var layer = operation.Layer;
        var file = await StorageFile.GetFileFromPathAsync(operation.ImagePath);
        using var stream = await file.OpenReadAsync();

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);

        var image = new Image
        {
            Source = bitmap,
            Width = layer.Transform.Width,
            Height = layer.Transform.Height,
            Opacity = operation.EffectiveOpacity,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(image, layer.Transform.X);
        Canvas.SetTop(image, layer.Transform.Y);

        image.RenderTransform = new CompositeTransform
        {
            CenterX = layer.Transform.Width / 2,
            CenterY = layer.Transform.Height / 2,
            Rotation = layer.Transform.Rotation,
            ScaleX = layer.Transform.FlipX ? -1 : 1,
            ScaleY = layer.Transform.FlipY ? -1 : 1
        };

        return image;
    }

    private async void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = Math.Clamp(e.NewValue / 100.0, 0.05, 2.0);
        if (ZoomLabel is not null)
        {
            ZoomLabel.Text = $"{e.NewValue:0}%";
        }

        if (_project is not null && ProjectCanvas is not null)
        {
            await ApplyZoomAsync();
        }
    }

    private Task ApplyZoomAsync()
    {
        if (_project is null)
        {
            return Task.CompletedTask;
        }

        var manifest = _project.Manifest;
        CanvasSurface.Width = manifest.Width * _zoom;
        CanvasSurface.Height = manifest.Height * _zoom;
        CanvasBorder.RenderTransform = new ScaleTransform { ScaleX = _zoom, ScaleY = _zoom };
        return Task.CompletedTask;
    }

    private async void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var availableWidth = Math.Max(100, ViewportHost.ActualWidth - 120);
        var availableHeight = Math.Max(100, ViewportHost.ActualHeight - 120);
        var fit = Math.Min(
            availableWidth / _project.Manifest.Width,
            availableHeight / _project.Manifest.Height);

        fit = Math.Clamp(fit, 0.05, 2.0);
        ZoomSlider.Value = fit * 100;
        await ApplyZoomAsync();
        CanvasScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };

        await dialog.ShowAsync();
        StatusText.Text = "Ready";
    }

    private sealed record LayerListItem(string Name, string KindGlyph, string Detail, string OpacityText);
}
