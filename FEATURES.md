# Collaborative SubEditor

Multiplayer submarine editing. Multiple players edit the same submarine simultaneously with real-time sync.

## Features

- **Co-editing** — entity placement, deletion, movement, property changes, and wiring sync between all connected editors
- **Cursors** — color-coded cursor positions shown for each editor
- **Entity locking** — selecting an entity locks it to you; others can't modify it until you deselect
- **Undo/redo** — standard undo works in multiplayer; remote edits recorded as undo steps
- **Test mode** — host starts a test round, all clients enter together, submarine state restored on return
- **Voice chat** — positioned relative to cursor locations
- **Permissions** — host can restrict editing, deleting, wiring, mass editing per-user via server settings

## Usage

**Host:** Open SubEditor → click "Host" → configure and start. A dedicated server launches and the host auto-connects.

**Client:** Open SubEditor → click "Join" → connect. The host's submarine syncs automatically.

**Dedicated server:** `DedicatedServer -subeditormode true -name "My Editor" -port 27015` — first player becomes host.

## New files

- `SubEditorNetworking.cs` — shared protocol, packet headers, permission flags, entity ownership
- `SubEditorNetworkingClient.cs` — client-side sync (cursors, entity notifications, submarine transfer)
- `SubEditorNetworkingServer.cs` — server-side session management, message routing, permission enforcement
- `SubEditorCommands.cs` — undo command types for collaborative operations
