using System.Text.Json.Serialization;

namespace Compositor.Core.Models;

public sealed class CompManifest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("colorSpace")]
    public string ColorSpace { get; set; } = "sRGB";

    [JsonPropertyName("documentID")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("resolution")]
    public double Resolution { get; set; } = 72;

    [JsonPropertyName("activeLayerID")]
    public string? ActiveLayerId { get; set; }

    [JsonPropertyName("layers")]
    public List<CompLayer> Layers { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

public sealed class CompLayer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Layer";

    [JsonPropertyName("imageFile")]
    public string? ImageFile { get; set; }

    [JsonPropertyName("maskFile")]
    public string? MaskFile { get; set; }

    [JsonPropertyName("maskEnabled")]
    public bool? MaskEnabled { get; set; }

    [JsonPropertyName("maskSourceID")]
    public string? MaskSourceId { get; set; }

    [JsonPropertyName("parentID")]
    public string? ParentId { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; } = true;

    [JsonPropertyName("isGroup")]
    public bool IsGroup { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1;

    [JsonPropertyName("blendMode")]
    public string BlendMode { get; set; } = "Normal";

    [JsonPropertyName("transform")]
    public CompTransform Transform { get; set; } = new();

    // Kept as raw JSON for forward-compatible metadata preservation while
    // V0.1 does not implement editable adjustment layers yet.
    [JsonPropertyName("adjustment")]
    public object? Adjustment { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

public sealed class CompTransform
{
    [JsonPropertyName("origin")]
    public double[] Origin { get; set; } = [0, 0];

    [JsonPropertyName("size")]
    public double[] Size { get; set; } = [0, 0];

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("flipX")]
    public bool FlipX { get; set; }

    [JsonPropertyName("flipY")]
    public bool FlipY { get; set; }

    [JsonPropertyName("sampling")]
    public string Sampling { get; set; } = "High quality";

    [JsonIgnore]
    public double X => Origin.Length >= 1 ? Origin[0] : 0;

    [JsonIgnore]
    public double Y => Origin.Length >= 2 ? Origin[1] : 0;

    [JsonIgnore]
    public double Width => Size.Length >= 1 ? Size[0] : 0;

    [JsonIgnore]
    public double Height => Size.Length >= 2 ? Size[1] : 0;

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
