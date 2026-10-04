using System.Collections.ObjectModel;
using System.Text.Json;
using Compositor.Core.Models;
using Compositor.Core.Services;
using Compositor.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
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
    private readonly AiBridgeHost _aiBridge;

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

    private EditorTool _tool = EditorTool.Move;
    private bool _panning;
    private global::Windows.Foundation.Point _panStart;
    private double _panHorizontalStart;
    private double _panVerticalStart;

    private CompManifest? _handleBefore;
    private string? _handleBeforeSelection;
    private TransformSnapshot? _handleOriginal;
    private string? _activeHandle;
    private double _handleDeltaX;
    private double _handleDeltaY;

    public MainWindow()
    {
        InitializeComponent();
        _aiBridge = new AiBridgeHost(HandleAiBridgeRequestAsync);
        Closed += MainWindow_Closed;
        LayersList.ItemsSource = _layerItems;
        ExtendsContentIntoTitleBar = false;
        UpdateCommandState();
        UpdateTitle();
        SetTool(EditorTool.Move);
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
        StatusCanvasText.Text = $"{project.Manifest.Width:N0} × {project.Manifest.Height:N0} px";

        PopulateLayers();
        UpdateProperties();
        UpdateCommandState();
        UpdateTitle();

        if (fit)
        {
            FitToViewport();
        }

        await RenderProjectAsync();
        UpdateAiBridgeDiscovery();
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

                var depth = GetLayerDepth(layer, project.Manifest.Layers);
                _layerItems.Add(new LayerListItem(
                    layer.Id,
                    $"{new string(' ', depth * 2)}{layer.Name}",
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
            RenameButton.IsEnabled = enabled;
            VisibilityButton.IsEnabled = enabled;
            RotateLeftButton.IsEnabled = enabled && layer?.IsGroup == false;
            RotateRightButton.IsEnabled = enabled && layer?.IsGroup == false;

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
        FlipHButton.IsEnabled = hasSelection && _session.SelectedLayer?.IsGroup == false;
        FlipVButton.IsEnabled = hasSelection && _session.SelectedLayer?.IsGroup == false;
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
            CanvasViewbox.Width = manifest.Width * _zoom;
            CanvasViewbox.Height = manifest.Height * _zoom;
            CanvasSurface.Width = CanvasViewbox.Width;
            CanvasSurface.Height = CanvasViewbox.Height;

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
        if (_tool != EditorTool.Move ||
            layer is null ||
            layer.IsGroup ||
            string.IsNullOrWhiteSpace(layer.ImageFile) ||
            !layer.IsVisible)
        {
            return;
        }

        var overlay = new Grid
        {
            Width = layer.Transform.Width,
            Height = layer.Transform.Height,
            IsHitTestVisible = true,
            Tag = "__selection",
            RenderTransform = CreateTransform(layer)
        };

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 58, 160, 255)),
            BorderThickness = new Thickness(Math.Max(1, 1 / _zoom)),
            IsHitTestVisible = false
        };
        overlay.Children.Add(border);

        var rotationLine = new Border
        {
            Width = Math.Max(1, 1 / _zoom),
            Height = 28 / _zoom,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 58, 160, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -28 / _zoom, 0, 0),
            IsHitTestVisible = false
        };
        overlay.Children.Add(rotationLine);

        AddTransformHandle(overlay, "nw", HorizontalAlignment.Left, VerticalAlignment.Top);
        AddTransformHandle(overlay, "ne", HorizontalAlignment.Right, VerticalAlignment.Top);
        AddTransformHandle(overlay, "se", HorizontalAlignment.Right, VerticalAlignment.Bottom);
        AddTransformHandle(overlay, "sw", HorizontalAlignment.Left, VerticalAlignment.Bottom);

        var rotate = CreateHandle("rotate");
        rotate.HorizontalAlignment = HorizontalAlignment.Center;
        rotate.VerticalAlignment = VerticalAlignment.Top;
        rotate.Margin = new Thickness(0, -40 / _zoom, 0, 0);
        overlay.Children.Add(rotate);

        Canvas.SetLeft(overlay, layer.Transform.X);
        Canvas.SetTop(overlay, layer.Transform.Y);
        ProjectCanvas.Children.Add(overlay);
    }

    private void AddTransformHandle(
        Grid overlay,
        string tag,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical)
    {
        var thumb = CreateHandle(tag);
        thumb.HorizontalAlignment = horizontal;
        thumb.VerticalAlignment = vertical;
        var half = 5 / _zoom;
        thumb.Margin = new Thickness(-half);
        overlay.Children.Add(thumb);
    }

    private Thumb CreateHandle(string tag)
    {
        var size = Math.Clamp(10 / _zoom, 8, 18);
        var thumb = new Thumb
        {
            Width = size,
            Height = size,
            Tag = tag,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 240, 245, 250)),
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 35, 120, 220)),
            BorderThickness = new Thickness(Math.Max(1, 1 / _zoom))
        };

        thumb.DragStarted += TransformHandle_DragStarted;
        thumb.DragDelta += TransformHandle_DragDelta;
        thumb.DragCompleted += TransformHandle_DragCompleted;
        return thumb;
    }

    private void TransformHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        var layer = _session.SelectedLayer;
        if (_session.Project is null ||
            layer is null ||
            layer.IsGroup ||
            sender is not Thumb thumb ||
            thumb.Tag is not string handle)
        {
            return;
        }

        _activeHandle = handle;
        _handleBefore = ManifestCloner.Clone(_session.Project.Manifest);
        _handleBeforeSelection = _session.SelectedLayerId;
        _handleOriginal = TransformSnapshot.From(layer.Transform);
        _handleDeltaX = 0;
        _handleDeltaY = 0;
    }

    private void TransformHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var layer = _session.SelectedLayer;
        if (layer is null || _handleOriginal is null || _activeHandle is null)
        {
            return;
        }

        _handleDeltaX += e.HorizontalChange / Math.Max(_zoom, 0.01);
        _handleDeltaY += e.VerticalChange / Math.Max(_zoom, 0.01);

        if (_activeHandle == "rotate")
        {
            var rotation = _handleOriginal.Rotation + (_handleDeltaX - _handleDeltaY) * 0.45;
            if (IsKeyDown(VirtualKey.Shift))
            {
                rotation = Math.Round(rotation / 15) * 15;
            }

            layer.Transform.Rotation = rotation;
        }
        else
        {
            ResizeFromHandle(layer, _activeHandle, _handleOriginal, _handleDeltaX, _handleDeltaY);
        }

        UpdateSelectedLayerVisuals();
        UpdateProperties();
        MarkModified();
    }

    private async void TransformHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_session.Project is not null && _handleBefore is not null)
        {
            _session.RecordExternalEdit(
                _activeHandle == "rotate" ? "Rotate Layer" : "Resize Layer",
                _handleBefore,
                _handleBeforeSelection);
        }

        _handleBefore = null;
        _handleBeforeSelection = null;
        _handleOriginal = null;
        _activeHandle = null;
        await RefreshDocumentAsync();
    }

    private static void ResizeFromHandle(
        CompLayer layer,
        string handle,
        TransformSnapshot original,
        double pointerDx,
        double pointerDy)
    {
        var sx = handle.Contains('e') ? 1.0 : -1.0;
        var sy = handle.Contains('s') ? 1.0 : -1.0;
        var radians = original.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var handlePoint = TransformPoint(
            original.CenterX,
            original.CenterY,
            sx * original.Width / 2,
            sy * original.Height / 2,
            cos,
            sin);

        var anchorPoint = TransformPoint(
            original.CenterX,
            original.CenterY,
            -sx * original.Width / 2,
            -sy * original.Height / 2,
            cos,
            sin);

        var candidateX = handlePoint.X + pointerDx;
        var candidateY = handlePoint.Y + pointerDy;
        var vx = candidateX - anchorPoint.X;
        var vy = candidateY - anchorPoint.Y;

        var localX = vx * cos + vy * sin;
        var localY = -vx * sin + vy * cos;

        var width = Math.Max(1, Math.Abs(localX));
        var height = Math.Max(1, Math.Abs(localY));

        if (IsKeyDown(VirtualKey.Shift))
        {
            var ratio = original.Width / Math.Max(1, original.Height);
            if (width / Math.Max(1, height) > ratio)
            {
                height = width / ratio;
            }
            else
            {
                width = height * ratio;
            }
        }

        var centerOffsetX = sx * width / 2;
        var centerOffsetY = sy * height / 2;
        var center = TransformPoint(
            anchorPoint.X,
            anchorPoint.Y,
            centerOffsetX,
            centerOffsetY,
            cos,
            sin);

        layer.Transform.Size = [Math.Round(width), Math.Round(height)];
        layer.Transform.Origin =
        [
            Math.Round(center.X - width / 2),
            Math.Round(center.Y - height / 2)
        ];
    }

    private static global::Windows.Foundation.Point TransformPoint(
        double originX,
        double originY,
        double localX,
        double localY,
        double cos,
        double sin)
        => new(
            originX + localX * cos - localY * sin,
            originY + localX * sin + localY * cos);

    private void UpdateSelectedLayerVisuals()
    {
        var layer = _session.SelectedLayer;
        if (layer is null)
        {
            return;
        }

        foreach (var child in ProjectCanvas.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is string tag &&
                string.Equals(tag, layer.Id, StringComparison.OrdinalIgnoreCase) &&
                child is Image image)
            {
                image.Width = layer.Transform.Width;
                image.Height = layer.Transform.Height;
                image.RenderTransform = CreateTransform(layer);
                Canvas.SetLeft(image, layer.Transform.X);
                Canvas.SetTop(image, layer.Transform.Y);
            }

            if (Equals(child.Tag, "__selection") && child is Grid overlay)
            {
                overlay.Width = layer.Transform.Width;
                overlay.Height = layer.Transform.Height;
                overlay.RenderTransform = CreateTransform(layer);
                Canvas.SetLeft(overlay, layer.Transform.X);
                Canvas.SetTop(overlay, layer.Transform.Y);
            }
        }
    }

    private void LayerImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_tool != EditorTool.Move ||
            sender is not FrameworkElement element ||
            element.Tag is not string layerId ||
            _session.Project is null)
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

        var desiredX = Math.Round(_dragOriginX + dx);
        var desiredY = Math.Round(_dragOriginY + dy);
        var snapped = SnapLayerPosition(layer, desiredX, desiredY);

        layer.Transform.Origin = [snapped.X, snapped.Y];

        Canvas.SetLeft(_dragElement, layer.Transform.X);
        Canvas.SetTop(_dragElement, layer.Transform.Y);
        DrawSnapGuides(snapped.GuideX, snapped.GuideY);
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
        ClearSnapGuides();

        if (_dragBefore is not null)
        {
            _session.RecordExternalEdit("Move Layer", _dragBefore, _dragBeforeSelection);
            _dragBefore = null;
            _dragBeforeSelection = null;
            MarkModified();
        }

        await RefreshDocumentAsync();
    }

    private async void AiBridge_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBridge.IsRunning)
        {
            var disable = new ContentDialog
            {
                Title = "AI Bridge",
                Content = "The local AI Bridge is enabled. Disabling it immediately disconnects MCP clients from this Compositor window.",
                PrimaryButtonText = "Disable",
                CloseButtonText = "Keep enabled",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = (Content as FrameworkElement)?.XamlRoot
            };

            if (await disable.ShowAsync() == ContentDialogResult.Primary)
            {
                await _aiBridge.StopAsync();
                AiBridgeStatus.Text = "Off";
                AiBridgeButton.Content = "AI";
                StatusText.Text = "AI Bridge disabled";
            }

            return;
        }

        var enable = new ContentDialog
        {
            Title = "Enable AI Bridge?",
            Content =
                "This enables a local-only Named Pipe that exposes explicit Compositor editing tools to the bundled MCP companion. " +
                "It does not open a network port and it does not send your document to an AI by itself. " +
                "Your MCP client decides which model receives document metadata and which tools it may call.",
            PrimaryButtonText = "Enable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };

        if (await enable.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _aiBridge.Start();
        UpdateAiBridgeDiscovery();
        AiBridgeStatus.Text = "On";
        AiBridgeButton.Content = "AI ✓";
        StatusText.Text = "AI Bridge enabled · MCP tools ready";
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        await _aiBridge.StopAsync();
    }

    private void UpdateAiBridgeDiscovery()
    {
        var project = _session.Project;
        _aiBridge.UpdateProject(
            project?.PackagePath,
            project?.Manifest.DocumentId,
            _isModified);
    }

    private Task<object?> HandleAiBridgeRequestAsync(AiBridgeRequest request)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Normal,
                async () =>
                {
                    try
                    {
                        completion.SetResult(await ExecuteAiBridgeRequestAsync(request));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                }))
        {
            completion.SetException(
                new InvalidOperationException("Compositor's UI thread is not available."));
        }

        return completion.Task;
    }

    private async Task<object?> ExecuteAiBridgeRequestAsync(AiBridgeRequest request)
    {
        var method = request.Method.Trim().ToLowerInvariant();

        if (method == "document.get")
        {
            return CreateAiDocumentSnapshot();
        }

        if (method == "layers.list")
        {
            EnsureAiProject();
            return _session.Project!.Manifest.Layers.Select(CreateAiLayerSnapshot).ToArray();
        }

        if (method == "layer.select")
        {
            var layer = SelectAiLayer(request.Parameters);
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(layer);
        }

        if (method == "layer.rename")
        {
            var layer = SelectAiLayer(request.Parameters);
            var name = RequiredString(request.Parameters, "name");
            _session.RenameSelected(name);
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(_session.SelectedLayer!);
        }

        if (method == "layer.visibility")
        {
            var layer = SelectAiLayer(request.Parameters);
            var visible = RequiredBoolean(request.Parameters, "visible");
            if (layer.IsVisible != visible)
            {
                _session.ToggleSelectedVisibility();
                MarkModified();
                await RefreshDocumentAsync();
            }

            return CreateAiLayerSnapshot(_session.SelectedLayer!);
        }

        if (method == "layer.opacity")
        {
            SelectAiLayer(request.Parameters);
            var opacity = RequiredDouble(request.Parameters, "opacity");
            _session.SetSelectedOpacity(opacity);
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(_session.SelectedLayer!);
        }

        if (method == "layer.transform")
        {
            var layer = SelectAiLayer(request.Parameters);
            if (layer.IsGroup)
            {
                throw new InvalidOperationException("Group transforms are not implemented yet.");
            }

            var transform = layer.Transform;
            var x = OptionalDouble(request.Parameters, "x") ?? transform.X;
            var y = OptionalDouble(request.Parameters, "y") ?? transform.Y;
            var width = OptionalDouble(request.Parameters, "width") ?? transform.Width;
            var height = OptionalDouble(request.Parameters, "height") ?? transform.Height;
            var rotation = OptionalDouble(request.Parameters, "rotation") ?? transform.Rotation;

            _session.SetSelectedTransform(x, y, width, height, rotation);
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(_session.SelectedLayer!);
        }

        if (method == "layer.flip")
        {
            var layer = SelectAiLayer(request.Parameters);
            if (layer.IsGroup)
            {
                throw new InvalidOperationException("Groups cannot be flipped in this preview.");
            }

            var axis = RequiredString(request.Parameters, "axis").Trim().ToLowerInvariant();
            if (axis is not ("horizontal" or "vertical"))
            {
                throw new ArgumentException("axis must be 'horizontal' or 'vertical'.");
            }

            _session.ToggleSelectedFlip(axis == "horizontal");
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(_session.SelectedLayer!);
        }

        if (method == "layer.duplicate")
        {
            SelectAiLayer(request.Parameters);
            var duplicate = _session.DuplicateSelected();
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(duplicate);
        }

        if (method == "layer.delete")
        {
            var selected = SelectAiLayer(request.Parameters);
            var deletedId = selected.Id;
            _session.DeleteSelected();
            MarkModified();
            await RefreshDocumentAsync();
            return new { deleted = deletedId, selectedLayerId = _session.SelectedLayerId };
        }

        if (method == "layer.import")
        {
            EnsureAiProject();
            var path = RequiredString(request.Parameters, "path");
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("AI import requires an existing absolute local image path.", path);
            }

            var extension = Path.GetExtension(path);
            if (!ImageImportService.SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsupported image type '{extension}'.");
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            var imported = await _imageImporter.ImportAsync(file, _session.Project!);
            var layer = CreateLayer(imported, _session.Project!, fitToCanvas: true);

            if (TryString(request.Parameters, "name") is { } requestedName &&
                !string.IsNullOrWhiteSpace(requestedName))
            {
                layer.Name = requestedName.Trim();
            }

            _session.AddLayer(layer, "AI Import Image");
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(layer);
        }

        if (method == "group.add")
        {
            EnsureAiProject();
            var name = TryString(request.Parameters, "name");
            var group = _session.AddGroup(
                string.IsNullOrWhiteSpace(name) ? "Group" : name!);
            MarkModified();
            await RefreshDocumentAsync();
            return CreateAiLayerSnapshot(group);
        }

        if (method == "history.undo")
        {
            EnsureAiProject();
            var changed = _session.Undo();
            if (changed)
            {
                _isModified = true;
                await RefreshDocumentAsync();
            }

            return new { changed, selectedLayerId = _session.SelectedLayerId };
        }

        if (method == "history.redo")
        {
            EnsureAiProject();
            var changed = _session.Redo();
            if (changed)
            {
                _isModified = true;
                await RefreshDocumentAsync();
            }

            return new { changed, selectedLayerId = _session.SelectedLayerId };
        }

        if (method == "project.save")
        {
            EnsureAiProject();
            if (string.IsNullOrWhiteSpace(_savedProjectPath))
            {
                throw new InvalidOperationException(
                    "This document has never been saved. Use Save As in Compositor once before an AI can save it.");
            }

            await SaveToAsync(_savedProjectPath);
            return new { saved = true, path = _savedProjectPath };
        }

        throw new InvalidOperationException(
            $"Unknown AI bridge method '{request.Method}'.");
    }

    private object CreateAiDocumentSnapshot()
    {
        EnsureAiProject();
        var project = _session.Project!;
        return new
        {
            app = "Compositor for Windows",
            bridgeProtocol = 1,
            document = new
            {
                id = project.Manifest.DocumentId,
                projectPath = project.PackagePath,
                savedPath = _savedProjectPath,
                name = _displayName,
                modified = _isModified,
                width = project.Manifest.Width,
                height = project.Manifest.Height,
                resolution = project.Manifest.Resolution,
                colorSpace = project.Manifest.ColorSpace,
                activeLayerId = _session.SelectedLayerId,
                layerCount = project.Manifest.Layers.Count
            },
            layers = project.Manifest.Layers.Select(CreateAiLayerSnapshot).ToArray()
        };
    }

    private static object CreateAiLayerSnapshot(CompLayer layer)
        => new
        {
            id = layer.Id,
            name = layer.Name,
            parentId = layer.ParentId,
            isGroup = layer.IsGroup,
            visible = layer.IsVisible,
            opacity = layer.Opacity,
            blendMode = layer.BlendMode,
            imageFile = layer.ImageFile,
            maskFile = layer.MaskFile,
            maskEnabled = layer.MaskEnabled,
            maskSourceId = layer.MaskSourceId,
            transform = new
            {
                x = layer.Transform.X,
                y = layer.Transform.Y,
                width = layer.Transform.Width,
                height = layer.Transform.Height,
                rotation = layer.Transform.Rotation,
                flipX = layer.Transform.FlipX,
                flipY = layer.Transform.FlipY,
                sampling = layer.Transform.Sampling
            }
        };

    private CompLayer SelectAiLayer(JsonElement parameters)
    {
        EnsureAiProject();
        var layerId = RequiredString(parameters, "layerId");
        _session.Select(layerId);
        return _session.SelectedLayer
            ?? throw new KeyNotFoundException($"Layer '{layerId}' does not exist.");
    }

    private void EnsureAiProject()
    {
        if (_session.Project is null)
        {
            throw new InvalidOperationException(
                "No document is open in Compositor.");
        }
    }

    private static string RequiredString(JsonElement parameters, string property)
        => TryString(parameters, property)
            ?? throw new ArgumentException($"Missing required parameter '{property}'.");

    private static string? TryString(JsonElement parameters, string property)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool RequiredBoolean(JsonElement parameters, string property)
    {
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty(property, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        throw new ArgumentException($"Missing or invalid boolean parameter '{property}'.");
    }

    private static double RequiredDouble(JsonElement parameters, string property)
        => OptionalDouble(parameters, property)
            ?? throw new ArgumentException($"Missing or invalid numeric parameter '{property}'.");

    private static double? OptionalDouble(JsonElement parameters, string property)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            throw new ArgumentException($"Parameter '{property}' must be numeric.");
        }

        return number;
    }

    private async void FlipHorizontal_Click(object sender, RoutedEventArgs e)
    {
        _session.ToggleSelectedFlip(horizontal: true);
        MarkModified();
        await RefreshDocumentAsync();
    }

    private async void FlipVertical_Click(object sender, RoutedEventArgs e)
    {
        _session.ToggleSelectedFlip(horizontal: false);
        MarkModified();
        await RefreshDocumentAsync();
    }

    private async void ActualPixels_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Project is null)
        {
            return;
        }

        _zoom = 1;
        ZoomSlider.Value = 100;
        ZoomLabel.Text = "100%";
        StatusZoomText.Text = "100%";
        await ApplyZoomAsync();
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag &&
            Enum.TryParse<EditorTool>(tag, ignoreCase: true, out var tool))
        {
            SetTool(tool);
        }
    }

    private void SetTool(EditorTool tool)
    {
        _tool = tool;

        MoveToolButton.Opacity = tool == EditorTool.Move ? 1 : 0.68;
        HandToolButton.Opacity = tool == EditorTool.Hand ? 1 : 0.68;
        ZoomToolButton.Opacity = tool == EditorTool.Zoom ? 1 : 0.68;

        ToolHeaderTitle.Text = tool.ToString();
        ToolHeaderHint.Text = tool switch
        {
            EditorTool.Move => "Drag a layer to move · Blue handles resize · Top handle rotates · Arrow keys nudge",
            EditorTool.Hand => "Drag the workspace to pan · Space temporarily pans in the macOS version",
            EditorTool.Zoom => "Click to zoom in · Right-click to zoom out · Fit and 100% are in the top bar",
            _ => string.Empty
        };

        if (_session.Project is not null)
        {
            _ = RenderProjectAsync();
        }
    }

    private void CanvasScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CanvasScrollViewer);

        if (_tool == EditorTool.Hand && point.Properties.IsLeftButtonPressed)
        {
            _panning = true;
            _panStart = point.Position;
            _panHorizontalStart = CanvasScrollViewer.HorizontalOffset;
            _panVerticalStart = CanvasScrollViewer.VerticalOffset;
            CanvasScrollViewer.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        if (_tool == EditorTool.Zoom)
        {
            var factor = point.Properties.IsRightButtonPressed ? 0.8 : 1.25;
            ZoomSlider.Value = Math.Clamp(_zoom * factor * 100, ZoomSlider.Minimum, ZoomSlider.Maximum);
            e.Handled = true;
        }
    }

    private void CanvasScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning || _tool != EditorTool.Hand)
        {
            return;
        }

        var point = e.GetCurrentPoint(CanvasScrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dx = point.Position.X - _panStart.X;
        var dy = point.Position.Y - _panStart.Y;
        CanvasScrollViewer.ChangeView(
            Math.Max(0, _panHorizontalStart - dx),
            Math.Max(0, _panVerticalStart - dy),
            null,
            disableAnimation: true);
        e.Handled = true;
    }

    private void CanvasScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            CanvasScrollViewer.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void Viewport_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _session.Project is null ? "Open image" : "Add image layer";
        }
    }

    private async void Viewport_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var files = items.OfType<StorageFile>()
            .Where(file => ImageImportService.SupportedExtensions.Contains(
                Path.GetExtension(file.Name),
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            if (_session.Project is null)
            {
                var first = files[0];
                var size = await _imageImporter.ReadSizeAsync(first);
                var workingPath = CreateTemporaryProjectPath();
                var project = _session.CreateNew(
                    workingPath,
                    checked((int)size.Width),
                    checked((int)size.Height));
                var imported = await _imageImporter.ImportAsync(first, project);
                _session.AddLayer(CreateLayer(imported, project, fitToCanvas: false), "Open Image");
                _session.History.Reset();
                _displayName = Path.GetFileNameWithoutExtension(first.Name);
                _savedProjectPath = null;
                _isModified = true;
                files.RemoveAt(0);
            }

            foreach (var file in files)
            {
                var imported = await _imageImporter.ImportAsync(file, _session.Project!);
                _session.AddLayer(CreateLayer(imported, _session.Project!, fitToCanvas: true));
            }

            MarkModified();
            await RefreshDocumentAsync(fit: false);
            StatusText.Text = "Image drop imported";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could not import dropped image", FriendlyImageError(ex));
        }
    }

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_session.Project is null)
        {
            if (e.Key == VirtualKey.V) SetTool(EditorTool.Move);
            else if (e.Key == VirtualKey.H) SetTool(EditorTool.Hand);
            else if (e.Key == VirtualKey.Z) SetTool(EditorTool.Zoom);
            return;
        }

        var control = IsKeyDown(VirtualKey.Control);
        var shift = IsKeyDown(VirtualKey.Shift);

        if (control && e.Key == VirtualKey.S)
        {
            Save_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (control && e.Key == VirtualKey.Z)
        {
            if (shift) Redo_Click(sender, new RoutedEventArgs());
            else Undo_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (control && e.Key == VirtualKey.Y)
        {
            Redo_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Delete)
        {
            DeleteLayer_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (!control && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var step = shift ? 10 : 1;
            var dx = e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0;
            var dy = e.Key == VirtualKey.Up ? -step : e.Key == VirtualKey.Down ? step : 0;
            _session.NudgeSelected(dx, dy);
            MarkModified();
            await RefreshDocumentAsync();
            e.Handled = true;
            return;
        }

        if (!control && e.Key == VirtualKey.V) SetTool(EditorTool.Move);
        else if (!control && e.Key == VirtualKey.H) SetTool(EditorTool.Hand);
        else if (!control && e.Key == VirtualKey.Z) SetTool(EditorTool.Zoom);
        else if (control && e.Key == VirtualKey.Number0) Fit_Click(sender, new RoutedEventArgs());
        else if (control && e.Key == VirtualKey.Number1) ActualPixels_Click(sender, new RoutedEventArgs());
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private (double X, double Y, double? GuideX, double? GuideY) SnapLayerPosition(
        CompLayer moving,
        double desiredX,
        double desiredY)
    {
        if (_session.Project is null || Math.Abs(moving.Transform.Rotation % 360) > 0.001)
        {
            return (desiredX, desiredY, null, null);
        }

        var width = moving.Transform.Width;
        var height = moving.Transform.Height;
        var manifest = _session.Project.Manifest;
        var xs = new List<double> { 0, manifest.Width / 2.0, manifest.Width };
        var ys = new List<double> { 0, manifest.Height / 2.0, manifest.Height };

        foreach (var layer in manifest.Layers)
        {
            if (layer.IsGroup || string.Equals(layer.Id, moving.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            xs.Add(layer.Transform.X);
            xs.Add(layer.Transform.X + layer.Transform.Width / 2);
            xs.Add(layer.Transform.X + layer.Transform.Width);
            ys.Add(layer.Transform.Y);
            ys.Add(layer.Transform.Y + layer.Transform.Height / 2);
            ys.Add(layer.Transform.Y + layer.Transform.Height);
        }

        var tolerance = 10 / Math.Max(_zoom, 0.05);
        var xSnap = FindSnap([desiredX, desiredX + width / 2, desiredX + width], xs, tolerance);
        var ySnap = FindSnap([desiredY, desiredY + height / 2, desiredY + height], ys, tolerance);

        return (
            desiredX + xSnap.Delta,
            desiredY + ySnap.Delta,
            xSnap.Target,
            ySnap.Target);
    }

    private static (double Delta, double? Target) FindSnap(
        IEnumerable<double> guides,
        IEnumerable<double> targets,
        double tolerance)
    {
        var best = double.PositiveInfinity;
        double? targetValue = null;
        var delta = 0.0;

        foreach (var guide in guides)
        {
            foreach (var target in targets)
            {
                var candidate = target - guide;
                if (Math.Abs(candidate) <= tolerance && Math.Abs(candidate) < best)
                {
                    best = Math.Abs(candidate);
                    delta = candidate;
                    targetValue = target;
                }
            }
        }

        return (delta, targetValue);
    }

    private void DrawSnapGuides(double? x, double? y)
    {
        ClearSnapGuides();
        if (_session.Project is null)
        {
            return;
        }

        var stroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(220, 60, 170, 255));
        var thickness = Math.Max(1, 1 / _zoom);

        if (x is double gx)
        {
            ProjectCanvas.Children.Add(new Line
            {
                X1 = gx,
                X2 = gx,
                Y1 = 0,
                Y2 = _session.Project.Manifest.Height,
                Stroke = stroke,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                Tag = "__snapguide"
            });
        }

        if (y is double gy)
        {
            ProjectCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = _session.Project.Manifest.Width,
                Y1 = gy,
                Y2 = gy,
                Stroke = stroke,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                Tag = "__snapguide"
            });
        }
    }

    private void ClearSnapGuides()
    {
        foreach (var line in ProjectCanvas.Children
                     .OfType<FrameworkElement>()
                     .Where(x => Equals(x.Tag, "__snapguide"))
                     .ToList())
        {
            ProjectCanvas.Children.Remove(line);
        }
    }

    private static int GetLayerDepth(CompLayer layer, IReadOnlyList<CompLayer> layers)
    {
        var byId = layers.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var depth = 0;
        var parent = layer.ParentId;
        while (parent is not null && depth < 32 && byId.TryGetValue(parent, out var group))
        {
            depth++;
            parent = group.ParentId;
        }

        return depth;
    }

    private async void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = Math.Clamp(e.NewValue / 100.0, 0.05, 2.0);
        if (ZoomLabel is not null)
        {
            ZoomLabel.Text = $"{e.NewValue:0}%";
            StatusZoomText.Text = $"{e.NewValue:0}%";
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
        CanvasViewbox.Width = manifest.Width * _zoom;
        CanvasViewbox.Height = manifest.Height * _zoom;
        CanvasSurface.Width = CanvasViewbox.Width;
        CanvasSurface.Height = CanvasViewbox.Height;
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
        StatusZoomText.Text = $"{fit * 100:0}%";
    }

    private void MarkModified()
    {
        _isModified = true;
        UpdateCommandState();
        UpdateTitle();
        UpdateAiBridgeDiscovery();
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

    private enum EditorTool
    {
        Move,
        Hand,
        Zoom
    }

    private sealed record TransformSnapshot(
        double X,
        double Y,
        double Width,
        double Height,
        double Rotation)
    {
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;

        public static TransformSnapshot From(CompTransform transform)
            => new(
                transform.X,
                transform.Y,
                transform.Width,
                transform.Height,
                transform.Rotation);
    }

    private sealed record LayerListItem(
        string Id,
        string Name,
        string KindGlyph,
        string Detail,
        string OpacityText);

    private sealed record NewDocumentOptions(int Width, int Height, double Resolution);
}
