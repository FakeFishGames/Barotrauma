# Collaborative SubEditor

Multiplayer submarine editing for Barotrauma. Multiple people can work on the same submarine at the same time through real-time networking.

## What it does

**Real-time co-editing** — Multiple players connect to a server running in SubEditor mode and edit the same submarine simultaneously. Entity placement, deletion, movement, property changes, and wiring are all synced between clients.

**Cursor tracking** — Each editor's cursor position is shown to all others, color-coded per user. You can see where everyone is working.

**Entity locking** — When someone selects an entity, it becomes locked to them (shown in their color). Others can't move or modify it until the original editor deselects it. Prevents edit conflicts.

**Undo/redo** — The standard SubEditor undo system works in multiplayer. Remote edits from other players are recorded as undo steps on each client, so you can undo your own changes without affecting others.

**Test mode** — The host can start a test round from the SubEditor. All connected clients enter the game together with the current submarine. When test mode ends, everyone returns to the editor with their submarine state intact.

**Voice chat** — Standard Barotrauma voice chat works in the editor. Voice is positioned relative to cursor locations rather than character positions, so you hear people near where they're working.

**Permissions** — The host can restrict what other editors are allowed to do through a permission system (edit own/others' entities, delete, wire, mass edit). Permissions default to full access and can be restricted per-user through server settings.

## How it works

### Starting a session

**As host:**
1. Open the SubEditor (singleplayer)
2. Click "Host" in the top-right of the editor
3. Configure server name, port, password, and player count
4. Click "Start Hosting" — a dedicated server starts in the background
5. The host auto-connects as the first editor

**As client:**
1. Open the SubEditor
2. Click "Join" — this opens the server browser filtered to SubEditor sessions
3. Connect to the server
4. The host's submarine is automatically synced to your client

### Editing

Once connected, the editor works as normal. Place items, build structures, wire things, edit properties — everything syncs.

The host is the authoritative source. Saving and loading submarines, starting test mode, and certain administrative actions are host-only.

### Test mode

The host clicks the standard "Test" button. The server broadcasts a test-mode signal, saves the submarine to a temp file, and starts a sandbox round with it. All clients enter the game. When anyone exits via the pause menu's "Return to Editor" button, the server ends the round and all clients return to the SubEditor with the pre-test submarine state restored.

### Server mode

Servers can also launch directly in SubEditor mode via command line:

```
DedicatedServer -subeditormode true -name "My Editor" -port 27015
```

The first player to connect becomes the host.

## Architecture

### New files
- `SubEditorNetworking.cs` — Shared protocol: packet headers, serializable structs, permission flags, entity ownership/locking
- `SubEditorNetworkingClient.cs` — Client: cursor sync, entity notifications, submarine transfer
- `SubEditorNetworkingServer.cs` — Server: session lifecycle, message routing, permission enforcement, test mode orchestration
- `SubEditorCommands.cs` — Undo command types for collaborative operations (links, properties, wire nodes)

### Network protocol
Uses a `SUBEDITOR` packet header added to `ClientPacketHeader`/`ServerPacketHeader`. Sub-headers (`SubEditorPacketHeader`) distinguish message types: cursor position, entity placement, removal, movement, property changes, submarine sync, test mode signals, client list updates.

Submarine state is synced as gzip-compressed XML rather than file transfers. Entity edits are relayed through the server, which enforces permissions and entity locks before forwarding.

### Permission system
`SubEditorPermissions` is a flags enum with 9 individual flags covering editing, deleting, wiring, mass editing, and undo. The host always has all permissions. Other clients receive configurable default permissions when they join. The host can customize permissions per-user through the server settings SubEditor tab.

Permission changes are synced in real-time: when the host modifies a client's permissions, the change is sent to the server via a `SetPermissions` packet, the server updates its authoritative permission state and broadcasts the update to all clients. Permissions are enforced server-side — the server silently drops packets from clients who lack the required permission for an action.
