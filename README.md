# Compositor for Windows

> **Unofficial Windows port of [Compositor](https://github.com/robbietilton/Compositor).**
>
> This project is independent from the upstream Compositor project and is not an official Wonder Assembly LLC release.

Compositor for Windows is an open-source Windows image editor project based on the workflow, project format, and architecture ideas of Robbie Tilton's **Compositor** for macOS. The goal is to bring the same layer-first, Photoshop-familiar editing model to Windows while preserving interoperability with `.comp` projects wherever practical.

The Windows port is being developed with assistance from **OpenAI Codex**. All generated or ported code is reviewed as ordinary project code; Codex is a development tool, not a runtime dependency.

## Status

**Early V0.1 / bootstrap milestone.** This repository is intentionally starting small and testable rather than pretending to be a complete Photoshop replacement on day one.

The first milestone can:

- open a `.comp` package folder;
- parse and validate `manifest.json`;
- understand document size, layer order, folders, visibility, opacity, transforms, and image references;
- build an effective render plan that inherits folder visibility and opacity;
- display ordinary PNG image layers in a WinUI 3 canvas;
- show the layer stack and basic document metadata;
- reload the current project from disk so external tools/agents can modify it.

Current renderer limitation: **V0.1 renders image layers using Normal compositing only.** Masks, adjustment layers, clipping masks, advanced blend modes, effects, text, shapes, GPU compositing, PSD, and RAW are roadmap items.

## Why this exists

The upstream app currently targets macOS 26+ on Apple silicon. This port explores a native Windows implementation using modern Windows technologies while keeping the open `.comp` package model that makes Compositor especially friendly to scripts and AI coding/design agents.

A `.comp` project is fundamentally a folder containing a JSON manifest and image assets:

```text
Example.comp/
├── manifest.json
└── images/
    ├── <layer-id>.png
    └── <layer-id>.mask.png
```

That means an external agent can edit the project on disk and the editor can reload those changes without a proprietary plugin protocol.

## Technology

- **C# / .NET 10 LTS**
- **WinUI 3**
- **Windows App SDK 2.5.1**
- portable document/project logic in `Compositor.Core`
- Windows UI and rendering shell in `Compositor.Windows`
- planned native/GPU path: Direct3D 12 / Direct2D, with selected portable C/C++ algorithms where appropriate

## Repository layout

```text
src/
  Compositor.Core/       Portable .comp model, validation and render planning
  Compositor.Windows/    WinUI 3 desktop application
samples/
  Hello.comp/            Minimal project for smoke testing
docs/
  ARCHITECTURE.md
  PORTING.md
  V1-SCOPE.md
```

## Build requirements

- Windows 11 recommended
- Visual Studio 2026 or current Visual Studio with Windows App SDK tooling
- .NET SDK 10.0.401 or newer 10.0.x SDK
- x64 for the initial V0.1 target

From a Developer PowerShell:

```powershell
dotnet restore .\Compositor.Windows.sln
dotnet build .\Compositor.Windows.sln -c Debug
```

Then run `Compositor.Windows` from Visual Studio, or:

```powershell
dotnet run --project .\src\Compositor.Windows\Compositor.Windows.csproj
```

## First smoke test

1. Launch the app.
2. Choose **Open .comp**.
3. Select `samples\Hello.comp`.
4. The sample background and foreground layers should appear and the Layers panel should list both layers.
5. Edit `samples\Hello.comp\manifest.json`, change a layer opacity, save it, and click **Reload**.

## Upstream relationship and attribution

This project is based on and inspired by:

- **Compositor** by Robbie Tilton / Wonder Assembly LLC
- Upstream repository: https://github.com/robbietilton/Compositor
- Upstream commit used as the initial porting reference: `11d8d7a50992b24fd9a760a1c13b1c01b70aaf30` (Compositor 1.4.5 era)
- Upstream license: MIT

See [`NOTICE.md`](NOTICE.md) and [`LICENSE`](LICENSE).

The upstream name and any upstream branding remain associated with their respective owner. This repository deliberately does **not** copy the upstream application icon or branding assets.

## Development approach

We are not doing a blind line-for-line Swift-to-C# translation. Apple-only dependencies such as SwiftUI, AppKit, Core Image, Metal, Vision, and Accelerate are replaced behind Windows-native abstractions. Portable algorithms can be ported selectively, with upstream attribution retained when code is derived from upstream source.

See [`docs/PORTING.md`](docs/PORTING.md) for the mapping.

## Roadmap

The practical V1 target is a useful Windows editor rather than full Photoshop parity. The order is roughly:

1. `.comp` load/save + live reload
2. reliable raster canvas, transforms, zoom/pan
3. layer editing, reorder, folders, opacity
4. masks
5. core blend modes on GPU
6. brush/erase + undo/redo
7. crop, marquee/lasso, basic selections
8. Levels, Curves, Hue/Saturation, Exposure, Blur
9. text and vector shapes
10. layer effects
11. PSD import compatibility
12. agent-friendly live project updates

See [`ROADMAP.md`](ROADMAP.md) for milestones.

## License

MIT. See [`LICENSE`](LICENSE).

If code is copied or substantially derived from upstream Compositor, its original MIT copyright notice must remain intact. See [`NOTICE.md`](NOTICE.md).
