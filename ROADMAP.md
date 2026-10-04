# Roadmap

The roadmap intentionally prioritizes a stable image-editor core over feature count.

## M0 — Bootstrap

- [x] repository structure
- [x] MIT + upstream attribution
- [x] Codex-assisted-development disclosure
- [x] WinUI 3 shell
- [x] portable `.comp` manifest model
- [x] package validation
- [x] simple raster layer render plan
- [x] sample `.comp` project
- [ ] verify build on a Windows development machine

## M1 — Editable layer stack

- [ ] save `.comp`
- [ ] create new document
- [ ] add/remove raster layers
- [ ] reorder layers
- [ ] rename layers
- [ ] visibility toggles
- [ ] opacity editing
- [ ] folder creation and nesting
- [ ] move/scale/rotate/flip
- [ ] fit / 100% / zoom / pan
- [ ] undo/redo document commands
- [ ] file watcher + safe live reload

## M2 — Masks and compositing

- [ ] raster masks
- [ ] folder masks
- [ ] clipping masks (`maskSourceID`)
- [ ] GPU-backed compositing
- [ ] Photoshop-compatible core blend modes
- [ ] color-space baseline and premultiplied-alpha rules

## M3 — Raster editing

- [ ] brush
- [ ] eraser
- [ ] eyedropper
- [ ] gradient
- [ ] rectangle/ellipse marquee
- [ ] lasso
- [ ] magic wand
- [ ] selection feather/expand/contract
- [ ] crop
- [ ] clone stamp
- [ ] blur tool

## M4 — Adjustments and effects

- [ ] Levels
- [ ] Curves
- [ ] Hue/Saturation
- [ ] Exposure
- [ ] Black & White
- [ ] Color Balance
- [ ] Gradient Map
- [ ] Gaussian Blur
- [ ] Motion Blur
- [ ] Noise / Grain
- [ ] Stroke
- [ ] Drop Shadow
- [ ] Color Overlay
- [ ] Inner/Outer Glow

## M5 — Type, shapes, interoperability

- [ ] editable text
- [ ] vector rectangles / ellipses / lines
- [ ] PSD import baseline
- [ ] JPEG / PNG / WebP import/export
- [ ] SVG import baseline
- [ ] clipboard layer copy/paste

## M6 — Agent workflow

- [ ] atomic manifest writes
- [ ] watch `.comp` package changes
- [ ] external-change conflict handling
- [ ] agent-focused format documentation
- [ ] command-line project validator
- [ ] deterministic screenshot/export smoke tests

## Explicitly not V1 blockers

- full Camera Raw parity
- CMYK PSD
- PSB-scale extreme documents
- 30k × 30k performance parity
- advanced subject/object segmentation
- content-aware generation
- plugin marketplace
