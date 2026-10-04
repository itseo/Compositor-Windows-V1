using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Core.Models;

namespace Compositor.Core.Services;

public static class ManifestCloner
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CompManifest Clone(CompManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, Options);
        return JsonSerializer.Deserialize<CompManifest>(json, Options)
            ?? throw new InvalidOperationException("Could not clone the document manifest.");
    }

    public static CompLayer CloneLayer(CompLayer layer)
    {
        var json = JsonSerializer.Serialize(layer, Options);
        return JsonSerializer.Deserialize<CompLayer>(json, Options)
            ?? throw new InvalidOperationException("Could not clone the layer.");
    }

    public static bool Equivalent(CompManifest left, CompManifest right)
        => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options);
}
