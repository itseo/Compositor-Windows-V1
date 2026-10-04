# Architecture

## Design goal

Make the document model portable and deterministic while allowing the Windows renderer to evolve from a simple WinUI raster compositor into a GPU-backed editor without rewriting file-format logic.

```text
                 .comp package
                      │
                      ▼
              Compositor.Core
        ┌─────────────────────────┐
        │ manifest model          │
        │ validation              │
        │ hierarchy resolution    │
        │ effective render plan   │
        └────────────┬────────────┘
                     │
                     ▼
              Compositor.Windows
        ┌─────────────────────────┐
        │ WinUI shell             │
        │ canvas viewport         │
        │ layer panel             │
        │ Windows file pickers    │
        │ renderer adapter        │
        └────────────┬────────────┘
                     │
         V0.1        │       Later
        XAML Image   │  Direct3D/Direct2D
                     ▼
                  Display
```

## `Compositor.Core`

Responsibilities:

- deserialize supported `.comp` manifest versions;
- validate safe paths and document limits;
- resolve group ancestry;
- compute inherited visibility and opacity;
- expose a deterministic bottom-to-top list of raster draw operations;
- remain free from Windows UI types.

The core deliberately stores blend modes as strings initially. This lets us preserve upstream metadata before every mode has a Windows implementation.

## `Compositor.Windows`

Responsibilities:

- app/window lifecycle;
- folder/file pickers;
- viewport and zoom UI;
- Layers panel;
- current rendering implementation;
- Windows-native input and future GPU integration.

## Renderer evolution

### V0.1

Each raster render operation becomes a WinUI `Image` inside a `Canvas`. Position, dimensions, rotation, flip and effective opacity are applied. Non-Normal blend modes are flagged and temporarily displayed using Normal compositing.

### V1 renderer

Move compositing behind an `IRenderer`-style boundary and implement a Direct3D/Direct2D-backed surface. The document model should not care which renderer is active.

### Native algorithm layer

Selected upstream C algorithms can be brought across later when they are useful and portable. Any derived source retains upstream MIT attribution. We should not copy files merely to inflate feature count.

## Security boundaries

A `.comp` project is untrusted input.

Current core checks include:

- expected `format` value;
- supported manifest version ceiling;
- manifest size limit;
- positive bounded canvas dimensions;
- layer-count limit;
- unique layer IDs;
- valid parent group references;
- no hierarchy cycles;
- image file names must be simple filenames, not paths;
- referenced layer assets must exist under `images/`.

More upstream-equivalent limits will be added as save/edit support lands.
