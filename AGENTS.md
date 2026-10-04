# AGENTS.md

This repository is an unofficial Windows port of `robbietilton/Compositor`.

## Rules for Codex and other coding agents

1. Read `README.md`, `NOTICE.md`, `docs/ARCHITECTURE.md`, `docs/PORTING.md`, and `docs/V1-SCOPE.md` before substantial changes.
2. Keep `Compositor.Core` free of WinUI/AppKit-specific concepts. The core must remain testable independently from the UI.
3. Do not perform blind translations of Apple frameworks. Port behavior, not framework calls.
4. Any source code copied or substantially derived from upstream Compositor must retain the upstream MIT copyright and be called out in the commit/PR.
5. Do not copy upstream logos or branding assets.
6. `.comp` parsing is untrusted-input parsing. Reject traversal, missing files, oversized manifests, invalid dimensions, invalid parent references, and unsupported future versions.
7. Preserve bottom-to-top layer array order from the `.comp` manifest.
8. Keep rendering and document state separate. Rendering must consume a deterministic render plan generated from the document model.
9. Prefer small milestones that can be manually verified with `samples/Hello.comp`.
10. Update `ROADMAP.md` and this repository's documentation when architecture or supported format behavior changes.
11. Windows V1 targets x64 first. Do not add ARM64 complexity unless a milestone specifically calls for it.
12. New dependencies need a concrete reason. Prefer BCL + Windows App SDK until a renderer dependency is deliberately selected.
