using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Compositor.Mcp;

[McpServerToolType]
public static class CompositorTools
{
    [McpServerTool, Description("Checks whether Compositor for Windows has its local AI Bridge enabled and returns the active document summary.")]
    public static string GetConnection()
        => BridgeClient.GetConnectionSummary();

    [McpServerTool, Description("Returns the active Compositor document, canvas metadata, active layer and full layer stack as structured JSON.")]
    public static Task<string> GetDocument(CancellationToken cancellationToken)
        => BridgeClient.CallAsync("document.get", cancellationToken: cancellationToken);

    [McpServerTool, Description("Returns the current layer stack from bottom to top, including IDs, names, hierarchy, visibility, opacity, blend mode and transform.")]
    public static Task<string> ListLayers(CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layers.list", cancellationToken: cancellationToken);

    [McpServerTool, Description("Selects a layer in the open Compositor document by its layer ID.")]
    public static Task<string> SelectLayer(
        [Description("Layer UUID from list_layers/get_document.")] string layerId,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.select", new { layerId }, cancellationToken);

    [McpServerTool, Description("Renames a layer in the open Compositor document.")]
    public static Task<string> RenameLayer(
        [Description("Layer UUID.")] string layerId,
        [Description("New non-empty layer name.")] string name,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.rename", new { layerId, name }, cancellationToken);

    [McpServerTool, Description("Shows or hides a layer.")]
    public static Task<string> SetLayerVisibility(
        [Description("Layer UUID.")] string layerId,
        bool visible,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.visibility", new { layerId, visible }, cancellationToken);

    [McpServerTool, Description("Sets layer opacity from 0.0 to 1.0.")]
    public static Task<string> SetLayerOpacity(
        [Description("Layer UUID.")] string layerId,
        [Description("Opacity between 0 and 1.")] double opacity,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.opacity", new { layerId, opacity }, cancellationToken);

    [McpServerTool, Description("Moves, resizes or rotates a raster layer. Omit values that should remain unchanged.")]
    public static Task<string> SetLayerTransform(
        [Description("Layer UUID.")] string layerId,
        double? x = null,
        double? y = null,
        double? width = null,
        double? height = null,
        double? rotation = null,
        CancellationToken cancellationToken = default)
        => BridgeClient.CallAsync(
            "layer.transform",
            new { layerId, x, y, width, height, rotation },
            cancellationToken);

    [McpServerTool, Description("Flips a raster layer horizontally or vertically.")]
    public static Task<string> FlipLayer(
        [Description("Layer UUID.")] string layerId,
        [Description("Either horizontal or vertical.")] string axis,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.flip", new { layerId, axis }, cancellationToken);

    [McpServerTool, Description("Duplicates a layer or folder and returns the new layer ID.")]
    public static Task<string> DuplicateLayer(
        [Description("Layer UUID.")] string layerId,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.duplicate", new { layerId }, cancellationToken);

    [McpServerTool, Description("Deletes a layer or folder from the open document.")]
    public static Task<string> DeleteLayer(
        [Description("Layer UUID.")] string layerId,
        CancellationToken cancellationToken)
        => BridgeClient.CallAsync("layer.delete", new { layerId }, cancellationToken);

    [McpServerTool, Description("Imports a local PNG, JPEG, TIFF or HEIC file as a new raster layer in the active document.")]
    public static Task<string> ImportImage(
        [Description("Absolute local path to the image file.")] string path,
        [Description("Optional name for the created layer.")] string? name = null,
        CancellationToken cancellationToken = default)
        => BridgeClient.CallAsync("layer.import", new { path, name }, cancellationToken);

    [McpServerTool, Description("Creates a folder/group in the active Compositor document.")]
    public static Task<string> AddGroup(
        [Description("Optional folder name.")] string? name = null,
        CancellationToken cancellationToken = default)
        => BridgeClient.CallAsync("group.add", new { name }, cancellationToken);

    [McpServerTool, Description("Undoes the most recent editable operation in Compositor.")]
    public static Task<string> Undo(CancellationToken cancellationToken)
        => BridgeClient.CallAsync("history.undo", cancellationToken: cancellationToken);

    [McpServerTool, Description("Redoes the most recently undone operation in Compositor.")]
    public static Task<string> Redo(CancellationToken cancellationToken)
        => BridgeClient.CallAsync("history.redo", cancellationToken: cancellationToken);

    [McpServerTool, Description("Saves the current Compositor project to its existing .comp location. If it has never been saved, the user must choose Save As in the app first.")]
    public static Task<string> Save(CancellationToken cancellationToken)
        => BridgeClient.CallAsync("project.save", cancellationToken: cancellationToken);
}
