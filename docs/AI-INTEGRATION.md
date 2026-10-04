# AI integration

Compositor for Windows treats AI as an editor client, not as a special rendering engine.

The goal is that an AI can inspect and manipulate the same editable document the user sees, through explicit tools that map onto normal Compositor operations and Undo/Redo.

## Architecture

### 1. In-process AI Bridge

The Windows app can expose the active editor session through a local Named Pipe.

The bridge is:
- **Off by default.**
- Enabled explicitly from the **AI** button in the toolbar.
- Local to the current Windows session; it does not listen on a TCP port.
- Protected by a random 256-bit session token.
- Limited to explicit document/layer operations.
- Disabled immediately when the editor window closes or the user turns it off.

Discovery file: `%LOCALAPPDATA%\\CompositorWindows\\ai-bridge.json`

It contains the current pipe name, temporary session token, process ID, active project path and document ID. It is intended for the bundled MCP companion running as the same Windows user.

### 2. Compositor.Mcp.exe

`src/Compositor.Mcp` is a stdio MCP server using the official C# Model Context Protocol SDK.

It does **not** edit the UI directly and it does not need API keys. It translates MCP tool calls into local AI Bridge requests.

An MCP-capable client launches `Compositor.Mcp.exe`; the companion discovers the currently running Compositor window and connects to its local Named Pipe.

This keeps provider-specific AI SDKs out of the editor core.

### 3. AI provider / client

The actual model lives outside this trust boundary.

Examples:
- ChatGPT / OpenAI via MCP.
- Codex or another coding agent with MCP support.
- Claude or another MCP client.
- A local model client.
- A future built-in Compositor AI panel.

The editor never sends document data anywhere by merely enabling the bridge. A model only receives information when its MCP client calls a tool.

## MCP tools in V1

Read:
- `get_connection`
- `get_document`
- `list_layers`

Selection and layer editing:
- `select_layer`
- `rename_layer`
- `set_layer_visibility`
- `set_layer_opacity`
- `set_layer_transform`
- `flip_layer`
- `duplicate_layer`
- `delete_layer`
- `import_image`
- `add_group`
- `crop_canvas`

History and persistence:
- `undo`
- `redo`
- `save`

Every mutating tool uses the same `EditorSession` used by the UI. Normal validation and Undo/Redo therefore continue to apply.

## Example AI workflow

A user can ask the AI to make a logo smaller, move it near the top-right edge, lower the background opacity, and save.

A capable MCP client can:
1. Call `get_document`.
2. Identify the logo and background layer IDs.
3. Call `set_layer_transform` on the logo.
4. Call `set_layer_opacity` on the background.
5. Call `save`.

The visible canvas updates after each edit.

## Generated images

`import_image` accepts an **absolute local path** to PNG/JPEG/TIFF/HEIC.

An AI image generator can save an output to a local file and then ask Compositor to import it as an ordinary editable raster layer.

The AI Bridge does not itself download arbitrary URLs. Network acquisition belongs to the AI client/provider, preserving a clear boundary.

## Security rules

- No network listener in the Windows app.
- No API key is stored in the bridge.
- The bridge starts only after explicit user action.
- The bridge token changes every time it is enabled.
- The MCP companion exposes document operations, not shell execution.
- Import only accepts an existing absolute local image path and one of Compositor's allowed raster extensions.
- Save cannot invent a destination: an unsaved document must first be given a location by the user through **Save As**.
- Future destructive or broad operations should remain ordinary undoable editor commands.
- A future built-in provider panel must keep provider keys in Windows Credential Locker, never in `.comp` files or repository configuration.

## Planned AI tools

As Windows feature parity grows, MCP should expose the same capabilities: layer hierarchy, crop, selections, masks, brushes, blend modes, adjustment layers, text/shapes, filters, PSD/PSB, export, rendered previews for vision models, and batch transactions that collapse multi-step AI edits into one Undo operation.

## Built-in AI panel

The eventual in-app panel should be a client of the same tool layer rather than a second editor implementation. It can support OpenAI Responses API tool calling, MCP-capable providers, OpenAI-compatible local endpoints, vision context, and per-operation approval modes.

This separation is deliberate: **AI orchestration can change; document editing semantics must remain stable.**
