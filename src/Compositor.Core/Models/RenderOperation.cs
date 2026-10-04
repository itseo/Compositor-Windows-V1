namespace Compositor.Core.Models;

public sealed record RenderOperation(
    CompLayer Layer,
    string ImagePath,
    double EffectiveOpacity,
    bool EffectiveVisible,
    bool HasUnsupportedBlendMode);
