# macOS → Windows parity matrix

Source of truth: upstream Compositor main, currently pinned in NOTICE/README.

Legend:
- ✅ implemented in Windows
- 🟡 partial / preview quality
- ⬜ not implemented yet

## Layers

| Feature | Windows |
| --- | --- |
| Raster layers | ✅ |
| Folders/groups | 🟡 create + preserve hierarchy; drag/drop nesting pending |
| Layer opacity | ✅ |
| Layer visibility | ✅ |
| Rename | ✅ |
| Duplicate | ✅ |
| Reorder | ✅ |
| Drag/drop reorder/nest | ⬜ |
| Full Photoshop blend-mode set | ⬜ |
| Layer masks | ⬜ |
| Folder masks | ⬜ |
| Clipping masks | ⬜ |
| Adjustment layers | ⬜ |
| Layer effects | ⬜ |
| Merge Down / Merge Layers / Merge Group | ⬜ |
| Copy/paste whole layers and folders | ⬜ |
| Cross-project layer copy / drag | ⬜ |

## Transform

| Feature | Windows |
| --- | --- |
| Move | ✅ |
| Scale | 🟡 interactive corner handles + exact W/H |
| Rotate | 🟡 interactive rotation handle + exact angle |
| Flip horizontal / vertical | ✅ |
| Snapping to document/layer edges and centers | 🟡 |
| Snap guides | 🟡 |
| Exact X/Y/W/H/angle | ✅ |
| Arrow-key nudging | ✅ |
| Multi-layer transform | ⬜ |
| Folder transform | ⬜ |
| Free distort | ⬜ |
| Flip Canvas | ⬜ |

## Selections

| Feature | Windows |
| --- | --- |
| Rectangle marquee | ⬜ |
| Ellipse marquee | ⬜ |
| Freehand lasso | ⬜ |
| Polygonal lasso | ⬜ |
| Magic Wand | ⬜ |
| Object selection | ⬜ |
| Select Subject | ⬜ |
| Expand / Contract / Feather | ⬜ |
| Add / subtract selection | ⬜ |
| Move / duplicate selected pixels | ⬜ |
| Load layer/mask as selection | ⬜ |
| Content-Aware Fill | ⬜ |

## Painting and retouching

| Feature | Windows |
| --- | --- |
| Brush | ⬜ |
| Eraser mode | ⬜ |
| Spot Healing | ⬜ |
| Clone Stamp | ⬜ |
| Blur tool | ⬜ |
| Gradient tool | ⬜ |
| Editable shapes | ⬜ |
| Editable text | ⬜ |
| Eyedropper / color picker | ⬜ |

## Adjustments and filters

| Feature | Windows |
| --- | --- |
| Hue/Saturation | ⬜ |
| Levels / Auto Levels | ⬜ |
| Curves | ⬜ |
| Exposure | ⬜ |
| Gradient Map | ⬜ |
| Grain | ⬜ |
| Black & White | ⬜ |
| Color Balance | ⬜ |
| Invert | ⬜ |
| Gaussian Blur | ⬜ |
| Motion Blur | ⬜ |
| Noise | ⬜ |
| Vignette | ⬜ |
| Bloom / Glow | ⬜ |
| Tonal Contrast | ⬜ |
| Lens Correction | ⬜ |
| Remove Background | ⬜ |
| Camera Raw workflow | ⬜ |

## Canvas and files

| Feature | Windows |
| --- | --- |
| New canvas | ✅ |
| Open .comp | ✅ |
| Save / Save As .comp | ✅ |
| Preserve unknown .comp metadata | ✅ |
| PNG import | ✅ |
| JPEG import | ✅ |
| TIFF import | ✅ |
| HEIC import | 🟡 depends on Windows HEIF codec |
| File drag/drop import | 🟡 |
| SVG import | ⬜ |
| PSD / PSB import | ⬜ |
| Camera RAW import/develop | ⬜ |
| Multiple projects/tabs | ⬜ |
| Rulers / draggable guides | ⬜ |
| Layout grid | ⬜ |
| Crop | ⬜ |
| Canvas Size | ⬜ |
| Image Size | ⬜ |
| Trim | ⬜ |
| JPEG export + preview | ⬜ |
| Copy Merged | ⬜ |
| Background saving | ⬜ |
| Live external .comp refresh | 🟡 reload exists; watcher pending |
| Remappable keyboard shortcuts | ⬜ |

## Editor chrome

| Feature | Windows |
| --- | --- |
| Dark editor chrome | ✅ |
| 56 px tool rail matching upstream structure | 🟡 |
| Context-sensitive tool header | 🟡 |
| Centered zoomable canvas | 🟡 |
| Layers panel | 🟡 |
| Status bar | 🟡 |
| Move / Hand / Zoom tools | 🟡 |
| Fit / 100% / zoom slider | ✅ |
| Startup smoke test in CI | ✅ |

## Implementation order

The Windows port should prefer behavior parity over direct framework translation:

1. Editor chrome + transforms + layer hierarchy.
2. Selections and crop.
3. Brush/eraser + masks.
4. Blend modes and GPU renderer.
5. Adjustment layers and filters.
6. Text, shapes and gradients.
7. PSD/PSB, SVG and RAW.
8. Retouching/content-aware/object selection.
9. Multi-document tabs, rulers/guides/grid, export workflow.
10. Performance, GPU tiling, large documents, shortcut editor and polish.

This file should be updated whenever a feature changes state.
