# Collaborative Submarine Editor

A mod for Barotrauma that enables real-time collaborative editing of submarines over a network. Multiple players connect to a dedicated server and edit the same submarine simultaneously, with live cursor tracking, entity sync, per-user undo with wire tracking, voice chat, and multiplayer test mode.

## How It Works

1. **Host** opens the Submarine Editor and clicks "Host" to start a dedicated server in SubEditor mode.
   - Configure server name, password (optional), public/private visibility, and max players.
2. **Clients** click "Join" and enter the host's IP:port, or find the server in the server browser.
3. The server sends an `EnterSubEditor` message to each connecting client, switching them to the Sub Editor screen.
4. All edits (place, delete, move, property changes, flips, links, wires) are broadcast to other editors in real time.
5. Each user has their own undo stack. The host sees all users' stacks and can undo any user's changes.
6. The host can start a multiplayer **Test Mode** round where all editors spawn into the submarine as characters.
7. After testing, all clients return to the editor with their submarine state and undo history preserved.

## Features

- **Real-time entity sync**: Place, delete, move, flip, resize, and change properties — all synced instantly
- **Link sync**: Space+Click entity linking synced between all editors
- **Wire undo**: Full undo support for wire connections, disconnections, and node edits (add/move/remove nodes)
- **Per-user undo stacks**: Each user has their own tab; host sees all users' tabs
- **CAD-style selective undo**: Click any command in the undo history to remove it, with automatic cascade deletion of dependent commands
- **Undo subtabs**: "Edits" and "Wires" subtabs per user for organized undo history
- **Undo persists across test mode**: History is not cleared when entering/exiting test rounds
- **Password-protected hosting**: Standard Barotrauma server hosting with password, public/private toggle, max players
- **Live cursors**: See where other editors are pointing and selecting
- **Voice chat**: Spatial audio based on cursor position (no characters needed)
- **Text chat**: Integrated into the editor, toggle with T
- **Test mode**: Host starts a multiplayer test round; all return to editor with state preserved

## File Changes

### New files (3)

| File | Purpose |
|------|---------|
| `Shared/Networking/SubEditorNetworking.cs` | Packet headers, serializable structs (`SubEditorCursorData`, `SubEditorSelectionData`, `SubEditorUser`), and `SubEditorSession` state container. |
| `Client/Networking/SubEditorNetworkingClient.cs` | Client-side networking: send/receive entity sync, cursor updates, test mode lifecycle, submarine XML sync, session management. |
| `Server/Networking/SubEditorNetworkingServer.cs` | Server-side networking: session management, entity relay, cursor relay with authoritative session ID, submarine XML sync, test mode start/return. `GameServer` is made `partial` so this file extends it. |

### Modified shared files (3)

| File | Change |
|------|--------|
| `Networking/NetworkMember.cs` | Added `SUBEDITOR` entry to `ClientPacketHeader` and `ServerPacketHeader` enums. |
| `GameSession/GameModes/GameModePreset.cs` | Added `SubEditor` game mode preset (non-votable). |
| `Map/Levels/Level.cs` | Added `IsSubEditorTestMode` static flag. When set, zeroes out level content counts (wrecks, ruins, caves, etc.) and skips outpost/corpse creation for fast test rounds. |

### Modified client files (13)

| File | Change |
|------|--------|
| `Screens/SubEditorScreen.cs` | Host/Join dialogs (with password, public, max players), collaborative user panel, cursor drawing, entity/property/transform/link sync hooks, test mode with XML snapshot preservation, chat, per-user undo tabs with subtabs (Edits/Wires), CAD-style selective undo, wire undo integration, Undo Latest button. |
| `Networking/GameClient.cs` | Handles `SubEditorPacketHeader` messages. Skips hash checks in SubEditor test mode. Returns to SubEditor on disconnect. Clears `Level.IsSubEditorTestMode` on EndGame. Skips end-round camera transition for test mode returns. |
| `Networking/Client.cs` | Positions voice audio relative to cursor delta in SubEditor mode (spatial audio without characters). |
| `Networking/Voip/VoipClient.cs` | Disables radio/muffle filters in SubEditor mode, sets generous audio range. |
| `SubEditorCommands.cs` | `AuthorSessionId` on `Command` base class. Read-only accessors on all command types. `GetAffectedEntityIds()` for dependency tracking. `WireCommand` class for wire connection/disconnection/node undo. |
| `GUI/GUI.cs` | "Return to Editor" button in pause menu during SubEditor test mode. |
| `GUI/ChatBox.cs` | Null-safe `CrewManager?.ReportButtonFrame` access (SubEditor uses ChatBox without a CrewManager). |
| `Screens/NetLobbyScreen.cs` | Filter non-votable game modes from lobby mode list (prevents SubEditor from appearing with missing icon style). |
| `Items/Item.cs` | Call `SyncLinkedEntityState()` after Space+Click link toggle. |
| `Map/Hull.cs` | Call `SyncLinkedEntityState()` after Space+Click link toggle. |
| `Map/WayPoint.cs` | Call `SyncLinkedEntityState()` after Space+Click link toggle. |
| `Items/Components/Signal/Connection.cs` | `OnSubEditorWireConnected` and `OnSubEditorWireDisconnected` static callbacks for wire undo tracking. |
| `Items/Components/Signal/Wire.cs` | `OnSubEditorNodeMoved`, `OnSubEditorNodeAdded`, `OnSubEditorNodeRemoved` static callbacks for wire node undo tracking. |

### Modified server files (3)

| File | Change |
|------|--------|
| `Networking/GameServer.cs` | On client connect in SubEditor mode: starts or joins session, grants host permissions. `SubEditor` game mode in `TryStartGame`. `EndGame` returns to SubEditor instead of lobby. Dispatches `SUBEDITOR` packet header to `SubEditorNetworkingServer`. |
| `GameMain.cs` | `IsSubEditorMode` property. `-subeditormode` and `-requireauthentication` CLI flags. Suppresses server list registration in SubEditor mode (unless public). |
| `DebugConsole.cs` | Server console commands: `subeditor_status`, `subeditor_sethost`, `subeditor_starttest`, `subeditor_endtest`. |

## Testing

See [BUILD_AND_TEST.md](BUILD_AND_TEST.md) for detailed build commands and testing procedures.
