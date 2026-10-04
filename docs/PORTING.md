# Porting strategy

The goal is behavioral compatibility, not source-level imitation of Apple frameworks.

| Upstream macOS technology | Windows direction |
| --- | --- |
| Swift / SwiftUI | C# / WinUI 3 |
| AppKit | Windows App SDK / Win32 interop |
| Metal | Direct3D 12 / DirectCompute |
| Core Graphics | Direct2D / custom raster layer |
| Core Image | GPU shaders + explicit image-processing pipeline |
| Vision | Windows ML / ONNX Runtime / DirectML when needed |
| Accelerate / vImage | SIMD/native C++ or tuned portable C |
| ImageIO | Windows Imaging Component + dedicated decoders as needed |
| `.comp` package | preserve compatible package/manifest semantics |

## What can be shared conceptually

- document/layer model;
- project schema;
- transform semantics;
- masks and clipping-mask semantics;
- adjustment parameter semantics;
- blend-mode math;
- portable C image algorithms;
- validation rules;
- user workflow and keyboard conventions where appropriate.

## What must be rewritten

- app lifecycle and windows;
- menus, panels and controls;
- GPU renderer;
- color/image framework integration;
- Apple Vision features;
- pasteboard/drag-and-drop implementation;
- file coordination/watchers;
- update/notarization pipeline.

## Upstream reference baseline

Initial architecture reference:

`robbietilton/Compositor@11d8d7a50992b24fd9a760a1c13b1c01b70aaf30`

As the upstream evolves, changes should be reviewed deliberately rather than merged blindly.
