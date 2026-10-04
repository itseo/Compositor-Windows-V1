using System.Collections.ObjectModel;
using Compositor.Core.Models;
using Compositor.Core.Services;
using Compositor.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Compositor.Windows;

public sealed partial class MainWindow : Window
{
    private readonly CompProjectLoader _loader = new();
    private readonly CompProjectStore _store = new();
    private readonly RenderPlanBuilder _renderPlanBuilder = new();
    private readonly EditorSession _session = new();
    private readonly ImageImportService _imageImporter = new();
    private readonly ObservableCollection<LayerListItem> _layerItems = [];

    private string? _savedProjectPath;
    private string _displayName = "Untitled";
    private double _zoom = 0.5;
    private bool _isRendering;
    private bool _updatingLayerList;
    private bool _updatingProperties;
    private bool _isModified;

    private bool _draggingLayer;
    private string? _dragLayerId;
    private global::Windows.Foundation.Point _dragStart;
    private double _dragOriginX;
    private double _dragOriginY;
    private CompManifest? _dragBefore;
    private string? _dragBeforeSelection;
    private FrameworkElement? _dragElement;

    public MainWindow()
    {
        InitializeComponent();
        LayersList.ItemsSource = _layerItems;
        ExtendsContentIntoTitleBar = false;
        UpdateCommandState();
        UpdateTitle();
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var options = await ShowNewDocumentDialogAsync();
        if (options is null)
        {
            return;
        }

        try
        {
            var workingPath = CreateTemporaryProjectPath();
            _session.CreateNew(workingPath, options.Width, options.Height, options.Resolution);
            _savedProjectPath = null;
            _displayName = "Untitled";
            _isModified = true;
            await RefreshDocumentAsync(fit: true);
            StatusText.Text = "New document created";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not create document", ex.Message);
        }
    }

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var file = await PickImageAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"Opening {file.Name}…";
            var size = await _imageImporter.ReadSizeAsync(file);
            if (size.Width == 0 || size.Height == 0 ||
                size.Width > CompProjectLoader.MaxCanvasSide ||
                size.Height > CompProjectLoader.MaxCanvasSide)
            {
                throw new InvalidDataException("The image dimensions are outside the supported range.");
            }

            var workingPath = CreateTemporaryProjectPath();
            var project = _session.CreateNew(
                workingPath,
                checked((int)size.Width),
                checked((int)size.Height));

            var imported = await _imageImporter.ImportAsync(file, project);
            _session.AddLayer(CreateLayer(imported, project, fitToCanvas: false), "Open Image");
            _session.History.Reset();

            _savedProjectPath = null;
            _displayName = Path.GetFileNameWithoutExtension(file.Name);
            _isModified = true;
            await RefreshDocumentAsync(fit: true);
            StatusText.Text = $"Opened {file.Name}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not open image", FriendlyImageError(ex));
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        try
        {
            var picker = CreateFolderPicker();
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

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.HasProject)
        {
            return;
        }

        if (_savedProjectPath is null)
        {
            await SaveAsAsync();
            return;
        }

        await SaveToAsync(_savedProjectPath);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsAsync();
    }

    private async Task SaveAsAsync()
    {
        if (_session.Project is null)
        {
            return;
        }

        try
        {
            var picker = CreateFolderPicker();
            var parent = await picker.PickSingleFolderAsync();
            if (parent is null)
            {
                return;
            }

            var name = await AskForTextAsync(
                "Save project",
                "Project name",
                string.IsNullOrWhiteSpace(_displayName) ? "Untitled" : _displayName,
                "Save");

            if (name is null)
            {
                return;
            }

            name = name.Trim();
            if (name.EndsWith(".comp", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^5];
            }

            if (string.IsNullOrWhiteSpace(name) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("Choose a valid project name.");
            }

            var destination = Path.Combine(parent.Path, name + ".comp");
            if (Directory.Exists(destination))
            {
                var replace = await ConfirmAsync(
                    "Replace project?",
                    $"A project named '{name}.comp' already exists in this folder.",
                    "Replace");

                if (!replace)
                {
                    return;
                }
            }

            await SaveToAsync(destination);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not save project", ex.Message);
        }
    }

    private async Task SaveToAsync(string destination)
    {
        if (_session.Project is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Saving…";
            await _store.SaveAsync(_session.Project, destination);
            _savedProjectPath = _session.Project.PackagePath;
            _displayName = Path.GetFileNameWithoutExtension(_savedProjectPath);
            _isModified = false;
            UpdateTitle();
            StatusText.Text = $"Saved {_displayName}.comp";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not save project", ex.Message);
        }
    }

    private async void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null)
        {
            return;
        }

        var file = await PickImageAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"Importing {file.Name}…";
            var imported = await _imageImporter.ImportAsync(file, _session.Project);
            _session.AddLayer(CreateLayer(imported, _session.Project, fitToCanvas: true));
            MarkModified();
            await RefreshDocumentAsync();
            StatusText.Text = $"Added {file.Name}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not add image layer", FriendlyImageError(ex));
        }
    }

    private async void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null)
        {
            return;
        }

        try
        {
            _session.AddGroup();
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not add group", ex.Message);
        }
    }

    private async void DuplicateLayer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.DuplicateSelected();
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not duplicate layer", ex.Message);
        }
    }

    private async void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.DeleteSelected();
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not delete layer", ex.Message);
        }
    }

    private async void LayerUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.MoveSelectedInStack(+1);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not move layer", ex.Message);
        }
    }

    private async void LayerDown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.MoveSelectedInStack(-1);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not move layer", ex.Message);
        }
    }

    private async void RenameLayer_Click(object sender, RoutedEventArgs e)
    {
        var layer = _session.SelectedLayer;
        if (layer is null)
        {
            return;
        }

        var name = await AskForTextAsync("Rename layer", "Name", layer.Name, "Rename");
        if (name is null)
        {
            return;
        }

        try
        {
            _session.RenameSelected(name);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not rename layer", ex.Message);
        }
    }

    private async void ToggleVisibility_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.ToggleSelectedVisibility();
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not change visibility", ex.Message);
        }
    }

    private async void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        await RotateSelectedAsync(-15);
    }

    private async void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        await RotateSelectedAsync(15);
    }

    private async Task RotateSelectedAsync(double degrees)
    {
        var layer = _session.SelectedLayer;
        if (layer is null || layer.IsGroup)
        {
            return;
        }

        try
        {
            var t = layer.Transform;
            _session.SetSelectedTransform(t.X, t.Y, t.Width, t.Height, t.Rotation + degrees);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not rotate layer", ex.Message);
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null || !_session.Undo())
        {
            return;
        }

        _isModified = true;
        await RefreshDocumentAsync();
        StatusText.Text = "Undo";
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null || !_session.Redo())
        {
            return;
        }

        _isModified = true;
        await RefreshDocumentAsync();
        StatusText.Text = "Redo";
    }

    private async void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLayerList)
        {
            return;
        }

        var item = LayersList.SelectedItem as LayerListItem;
        _session.Select(item?.Id);
        UpdateProperties();
        UpdateCommandState();
        await RenderProjectAsync();
    }

    private async void TransformBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingProperties)
        {
            return;
        }

        var layer = _session.SelectedLayer;
        if (layer is null || layer.IsGroup)
        {
            return;
        }

        var values = new[] { XBox.Value, YBox.Value, WidthBox.Value, HeightBox.Value, RotationBox.Value };
        if (values.Any(double.IsNaN))
        {
            return;
        }

        try
        {
            _session.SetSelectedTransform(
                XBox.Value,
                YBox.Value,
                WidthBox.Value,
                HeightBox.Value,
                RotationBox.Value);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not transform layer", ex.Message);
        }
    }

    private async void OpacityBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingProperties || double.IsNaN(OpacityBox.Value) || _session.SelectedLayer is null)
        {
            return;
        }

        try
        {
            _session.SetSelectedOpacity(OpacityBox.Value / 100.0);
            MarkModified();
            await RefreshDocumentAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not change opacity", ex.Message);
        }
    }

    private async Task LoadProjectAsync(string packagePath)
    {
        StatusText.Text = "Loading project…";
        var project = await _loader.LoadAsync(packagePath);
        _session.Install(project);

        _savedProjectPath = project.PackagePath;
        _displayName = Path.GetFileNameWithoutExtension(project.PackagePath);
        _isModified = false;

        await RefreshDocumentAsync(fit: true);
        StatusText.Text = $"Opened {_displayName}.comp";
    }

    private async Task RefreshDocumentAsync(bool fit = false)
    {
        var project = _session.Project;
        if (project is null)
        {
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        DocumentInfo.Text =
            $"{project.Manifest.Width:N0} × {project.Manifest.Height:N0} · {project.Manifest.Resolution:0.#} ppi · {project.Manifest.Layers.Count} layer(s)";

        PopulateLayers();
        UpdateProperties();
        UpdateCommandState();
        UpdateTitle();

        if (fit)
        {
            FitToViewport();
        }

        await RenderProjectAsync();
    }

    private void PopulateLayers()
    {
        _updatingLayerList = true;
        try
        {
            _layerItems.Clear();
            var project = _session.Project;
            if (project is null)
            {
                LayersList.SelectedItem = null;
                return;
            }

            foreach (var layer in project.Manifest.Layers.AsEnumerable().Reverse())
            {
                var detail = layer.IsGroup
                    ? "Folder"
                    : string.IsNullOrWhiteSpace(layer.ImageFile)
                        ? "Non-raster layer"
                        : layer.IsVisible ? layer.BlendMode : "Hidden";

                _layerItems.Add(new LayerListItem(
                    layer.Id,
                    layer.Name,
                    layer.IsGroup ? "▾" : layer.IsVisible ? "▣" : "□",
                    detail,
                    $"{layer.Opacity:P0}"));
            }

            LayersList.SelectedItem = _layerItems.FirstOrDefault(
                x => string.Equals(x.Id, _session.SelectedLayerId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _updatingLayerList = false;
        }
    }

    private void UpdateProperties()
    {
        _updatingProperties = true;
        try
        {
            var layer = _session.SelectedLayer;
            var enabled = layer is not null;
            PropertiesPanel.IsEnabled = enabled;

            if (layer is null)
            {
                XBox.Value = double.NaN;
                YBox.Value = double.NaN;
                WidthBox.Value = double.NaN;
                HeightBox.Value = double.NaN;
                RotationBox.Value = double.NaN;
                OpacityBox.Value = double.NaN;
                return;
            }

            var transformEnabled = !layer.IsGroup;
            XBox.IsEnabled = transformEnabled;
            YBox.IsEnabled = transformEnabled;
            WidthBox.IsEnabled = transformEnabled;
            HeightBox.IsEnabled = transformEnabled;
            RotationBox.IsEnabled = transformEnabled;

            XBox.Value = layer.Transform.X;
            YBox.Value = layer.Transform.Y;
            WidthBox.Value = layer.Transform.Width;
            HeightBox.Value = layer.Transform.Height;
            RotationBox.Value = layer.Transform.Rotation;
            OpacityBox.Value = layer.Opacity * 100;
        }
        finally
        {
            _updatingProperties = false;
        }
    }

    private void UpdateCommandState()
    {
        var hasProject = _session.Project is not null;
        var hasSelection = _session.SelectedLayer is not null;

        SaveButton.IsEnabled = hasProject;
        SaveAsButton.IsEnabled = hasProject;
        AddLayerButton.IsEnabled = hasProject;
        LayerAddButton.IsEnabled = hasProject;
        AddGroupButton.IsEnabled = hasProject;

        UndoButton.IsEnabled = hasProject && _session.History.CanUndo;
        RedoButton.IsEnabled = hasProject && _session.History.CanRedo;

        DuplicateButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        LayerUpButton.IsEnabled = hasSelection;
        LayerDownButton.IsEnabled = hasSelection;
        RenameButton.IsEnabled = hasSelection;
    }

    private async Task RenderProjectAsync()
    {
        if (_session.Project is null || _isRendering)
        {
            return;
        }

        _isRendering = true;
        try
        {
            StatusText.Text = "Rendering…";
            ProjectCanvas.Children.Clear();

            var project = _session.Project;
            var manifest = project.Manifest;

            ProjectCanvas.Width = manifest.Width;
            ProjectCanvas.Height = manifest.Height;
            CanvasBorder.Width = manifest.Width;
            CanvasBorder.Height = manifest.Height;
            CanvasSurface.Width = manifest.Width * _zoom;
            CanvasSurface.Height = manifest.Height * _zoom;
            CanvasBorder.RenderTransform = new ScaleTransform { ScaleX = _zoom, ScaleY = _zoom };
            CanvasBorder.RenderTransformOrigin = new global::Windows.Foundation.Point(0, 0);

            var operations = _renderPlanBuilder.Build(project);
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

            DrawSelectionOverlay();

            var rendered = operations.Count(x => x.EffectiveVisible && x.EffectiveOpacity > 0);
            StatusText.Text = unsupportedBlendCount == 0
                ? $"Rendered {rendered} raster layer(s)"
                : $"Rendered {rendered} raster layer(s); {unsupportedBlendCount} blend mode(s) previewed as Normal";
        }
        finally
        {
            _isRendering = false;
        }
    }

    private async Task<Image> CreateLayerImageAsync(RenderOperation operation)
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
            IsHitTestVisible = true,
            Tag = layer.Id
        };

        Canvas.SetLeft(image, layer.Transform.X);
        Canvas.SetTop(image, layer.Transform.Y);
        image.RenderTransform = CreateTransform(layer);

        image.PointerPressed += LayerImage_PointerPressed;
        image.PointerMoved += LayerImage_PointerMoved;
        image.PointerReleased += LayerImage_PointerReleased;
        image.PointerCaptureLost += LayerImage_PointerCaptureLost;

        return image;
    }

    private static CompositeTransform CreateTransform(CompLayer layer)
        => new()
        {
            CenterX = layer.Transform.Width / 2,
            CenterY = layer.Transform.Height / 2,
            Rotation = layer.Transform.Rotation,
            ScaleX = layer.Transform.FlipX ? -1 : 1,
            ScaleY = layer.Transform.FlipY ? -1 : 1
        };

    private void DrawSelectionOverlay()
    {
        var layer = _session.SelectedLayer;
        if (layer is null || layer.IsGroup || string.IsNullOrWhiteSpace(layer.ImageFile) || !layer.IsVisible)
        {
            return;
        }

        var border = new Border
        {
            Width = layer.Transform.Width,
            Height = layer.Transform.Height,
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 44, 142, 255)),
            BorderThickness = new Thickness(Math.Max(1, 1 / _zoom)),
            IsHitTestVisible = false,
            RenderTransform = CreateTransform(layer)
        };

        Canvas.SetLeft(border, layer.Transform.X);
        Canvas.SetTop(border, layer.Transform.Y);
        ProjectCanvas.Children.Add(border);
    }

    private void LayerImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string layerId || _session.Project is null)
        {
            return;
        }

        _session.Select(layerId);
        PopulateLayers();
        UpdateProperties();
        UpdateCommandState();

        var layer = _session.SelectedLayer;
        if (layer is null || layer.IsGroup)
        {
            return;
        }

        var point = e.GetCurrentPoint(ProjectCanvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggingLayer = true;
        _dragLayerId = layer.Id;
        _dragStart = point.Position;
        _dragOriginX = layer.Transform.X;
        _dragOriginY = layer.Transform.Y;
        _dragBefore = ManifestCloner.Clone(_session.Project.Manifest);
        _dragBeforeSelection = _session.SelectedLayerId;
        _dragElement = element;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void LayerImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingLayer ||
            _dragElement is null ||
            _session.SelectedLayer is not { } layer ||
            !string.Equals(layer.Id, _dragLayerId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var point = e.GetCurrentPoint(ProjectCanvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dx = point.Position.X - _dragStart.X;
        var dy = point.Position.Y - _dragStart.Y;

        layer.Transform.Origin =
        [
            Math.Round(_dragOriginX + dx),
            Math.Round(_dragOriginY + dy)
        ];

        Canvas.SetLeft(_dragElement, layer.Transform.X);
        Canvas.SetTop(_dragElement, layer.Transform.Y);
        UpdateProperties();
        StatusText.Text = $"Moving {layer.Name} · X {layer.Transform.X:0} · Y {layer.Transform.Y:0}";
        e.Handled = true;
    }

    private async void LayerImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingLayer)
        {
            return;
        }

        if (sender is FrameworkElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        await FinishLayerDragAsync();
        e.Handled = true;
    }

    private async void LayerImage_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingLayer)
        {
            await FinishLayerDragAsync();
        }
    }

    private async Task FinishLayerDragAsync()
    {
        if (!_draggingLayer)
        {
            return;
        }

        _draggingLayer = false;
        _dragElement = null;
        _dragLayerId = null;

        if (_dragBefore is not null)
        {
            _session.RecordExternalEdit("Move Layer", _dragBefore, _dragBeforeSelection);
            _dragBefore = null;
            _dragBeforeSelection = null;
            MarkModified();
        }

        await RefreshDocumentAsync();
    }

    private async void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = Math.Clamp(e.NewValue / 100.0, 0.05, 2.0);
        if (ZoomLabel is not null)
        {
            ZoomLabel.Text = $"{e.NewValue:0}%";
        }

        if (_session.Project is not null && ProjectCanvas is not null)
        {
            await ApplyZoomAsync();
        }
    }

    private Task ApplyZoomAsync()
    {
        if (_session.Project is null)
        {
            return Task.CompletedTask;
        }

        var manifest = _session.Project.Manifest;
        CanvasSurface.Width = manifest.Width * _zoom;
        CanvasSurface.Height = manifest.Height * _zoom;
        CanvasBorder.RenderTransform = new ScaleTransform { ScaleX = _zoom, ScaleY = _zoom };
        return Task.CompletedTask;
    }

    private async void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null)
        {
            return;
        }

        FitToViewport();
        await ApplyZoomAsync();
        CanvasScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void FitToViewport()
    {
        if (_session.Project is null)
        {
            return;
        }

        var availableWidth = Math.Max(100, ViewportHost.ActualWidth - 120);
        var availableHeight = Math.Max(100, ViewportHost.ActualHeight - 120);
        var fit = Math.Min(
            availableWidth / _session.Project.Manifest.Width,
            availableHeight / _session.Project.Manifest.Height);

        fit = Math.Clamp(fit, 0.05, 2.0);
        _zoom = fit;
        ZoomSlider.Value = fit * 100;
        ZoomLabel.Text = $"{fit * 100:0}%";
    }

    private void MarkModified()
    {
        _isModified = true;
        UpdateCommandState();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = $"{(_isModified ? "*" : string.Empty)}{_displayName} — Compositor for Windows";
    }

    private CompLayer CreateLayer(ImportedRaster imported, CompProject project, bool fitToCanvas)
    {
        var width = (double)imported.PixelWidth;
        var height = (double)imported.PixelHeight;

        if (fitToCanvas)
        {
            var scale = Math.Min(
                1,
                Math.Min(project.Manifest.Width / width, project.Manifest.Height / height));
            width *= scale;
            height *= scale;
        }

        return new CompLayer
        {
            Id = imported.LayerId,
            Name = string.IsNullOrWhiteSpace(imported.Name) ? "Layer" : imported.Name,
            ImageFile = $"{imported.LayerId}.png",
            IsVisible = true,
            IsGroup = false,
            Opacity = 1,
            BlendMode = "Normal",
            Transform = new CompTransform
            {
                Origin =
                [
                    (project.Manifest.Width - width) / 2,
                    (project.Manifest.Height - height) / 2
                ],
                Size = [width, height],
                Rotation = 0,
                FlipX = false,
                FlipY = false,
                Sampling = "High quality"
            }
        };
    }

    private FileOpenPicker CreateImagePicker()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };

        foreach (var extension in ImageImportService.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializePicker(picker);
        return picker;
    }

    private async Task<StorageFile?> PickImageAsync()
        => await CreateImagePicker().PickSingleFileAsync();

    private FolderPicker CreateFolderPicker()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        return picker;
    }

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static string CreateTemporaryProjectPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompositorWindows", "Working");
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N") + ".comp");
    }

    private async Task<NewDocumentOptions?> ShowNewDocumentDialogAsync()
    {
        var width = new NumberBox
        {
            Header = "Width",
            Minimum = 1,
            Maximum = CompProjectLoader.MaxCanvasSide,
            Value = 1920,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var height = new NumberBox
        {
            Header = "Height",
            Minimum = 1,
            Maximum = CompProjectLoader.MaxCanvasSide,
            Value = 1080,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var resolution = new NumberBox
        {
            Header = "Resolution (ppi)",
            Minimum = 1,
            Maximum = 9600,
            Value = 72,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var content = new StackPanel { Spacing = 10, Width = 320 };
        content.Children.Add(width);
        content.Children.Add(height);
        content.Children.Add(resolution);

        var dialog = new ContentDialog
        {
            Title = "New document",
            Content = content,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary ||
            double.IsNaN(width.Value) ||
            double.IsNaN(height.Value) ||
            double.IsNaN(resolution.Value))
        {
            return null;
        }

        return new NewDocumentOptions(
            checked((int)Math.Round(width.Value)),
            checked((int)Math.Round(height.Value)),
            resolution.Value);
    }

    private async Task<string?> AskForTextAsync(
        string title,
        string header,
        string initial,
        string primaryText)
    {
        var box = new TextBox
        {
            Header = header,
            Text = initial,
            Width = 340
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? box.Text
            : null;
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_isModified)
        {
            return true;
        }

        return await ConfirmAsync(
            "Discard unsaved changes?",
            "The current document has changes that have not been saved.",
            "Discard");
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

    private static string FriendlyImageError(Exception ex)
    {
        if (ex.HResult == unchecked((int)0x88982F50) ||
            ex.Message.Contains("codec", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows could not decode this image. HEIC files require HEIF image support on the PC.";
        }

        return ex.Message;
    }

    private sealed record LayerListItem(
        string Id,
        string Name,
        string KindGlyph,
        string Detail,
        string OpacityText);

    private sealed record NewDocumentOptions(int Width, int Height, double Resolution);
}
