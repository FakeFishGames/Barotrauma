# Collaborative Submarine Editor

A mod for Barotrauma that enables real-time collaborative editing of submarines over a network. Multiple players connect to a dedicated server and edit the same submarine simultaneously, with live cursor tracking, entity sync, voice chat, and multiplayer test mode.

## How It Works

1. **Host** opens the Submarine Editor and clicks "Host" to start a dedicated server in SubEditor mode.
2. **Clients** click "Join" and enter the host's IP:port, or use "Join Game > Direct Connect" from the main menu.
3. The server sends an `EnterSubEditor` message to each connecting client, switching them to the Sub Editor screen.
4. All edits (place, delete, move, property changes, flips) are broadcast to other editors in real time.
5. The host can start a multiplayer **Test Mode** round where all editors spawn into the submarine as characters.
6. After testing, all clients return to the editor with their submarine state preserved.

## File Changes

### New files

| File | Purpose |
|------|---------|
| `Shared/Networking/SubEditorNetworking.cs` | Packet headers, serializable structs (`SubEditorCursorData`, `SubEditorSelectionData`, `SubEditorUser`), and `SubEditorSession` state container. |
| `Client/Networking/SubEditorNetworkingClient.cs` | Client-side networking: send/receive entity sync, cursor updates, test mode lifecycle, submarine file handling, session management. |
| `Server/Networking/SubEditorNetworkingServer.cs` | Server-side networking: session management, entity relay, cursor relay with authoritative session ID, submarine file transfer, test mode start/return. `GameServer` is made `partial` so this file extends it. |

### Modified shared files

| File | Change |
|------|--------|
| `Networking/NetworkMember.cs` | Added `SUBEDITOR` entry to `ClientPacketHeader` and `ServerPacketHeader` enums. |
| `GameSession/GameModes/GameModePreset.cs` | Added `SubEditor` game mode preset (non-votable). |
| `Map/Levels/Level.cs` | Added `IsSubEditorTestMode` static flag. When set, zeroes out level content counts (wrecks, ruins, caves, etc.) and skips outpost/corpse creation for fast test rounds. |

### Modified client files

| File | Change |
|------|--------|
| `Screens/SubEditorScreen.cs` | Host/Join buttons, collaborative user panel, cursor drawing for remote users, entity/property/transform sync hooks, test mode entry/exit with XML snapshot preservation, chat integration, real-time move tracking. |
| `Networking/GameClient.cs` | Handles `SubEditorPacketHeader` messages. Modified round-start to skip sub/shuttle/level hash checks in SubEditor test mode (the temp sub won't match stored hashes). Returns to SubEditor instead of lobby on disconnect. |
| `Networking/Client.cs` | Positions voice audio relative to cursor delta in SubEditor mode (spatial audio without characters). |
| `Networking/Voip/VoipClient.cs` | Disables radio/muffle filters in SubEditor mode, sets generous audio range. |
| `SubEditorCommands.cs` | Read-only accessors on `TransformCommand`, `AddOrDeleteCommand`, `PropertyCommand` so collaborative sync can read command data. |
| `GUI/GUI.cs` | "Return to Editor" button in pause menu during SubEditor test mode. |
| `GUI/ChatBox.cs` | Null-safe `CrewManager?.ReportButtonFrame` access (SubEditor uses ChatBox without a CrewManager). |

### Modified server files

| File | Change |
|------|--------|
| `Networking/GameServer.cs` | On client connect in SubEditor mode: starts or joins session, grants host permissions. `SubEditor` game mode in `TryStartGame`. `EndGame` returns to SubEditor instead of lobby. Dispatches `SUBEDITOR` packet header to `SubEditorNetworkingServer`. |
| `GameMain.cs` | `IsSubEditorMode` property. `-subeditormode` and `-requireauthentication` CLI flags. Suppresses server list registration in SubEditor mode. |
| `DebugConsole.cs` | Server console commands: `subeditor_status`, `subeditor_sethost`, `subeditor_starttest`, `subeditor_endtest`. |

## Testing

1. Build server: `dotnet build Barotrauma/BarotraumaServer/LinuxServer.csproj -c Release`
2. Build client: `dotnet build Barotrauma/BarotraumaClient/LinuxClient.csproj -c Release`
3. Run client, open Submarine Editor, click "Host" to start a session
4. Run second client, open Submarine Editor, click "Join" with the host's address
5. Both editors should see each other's cursors and entity changes in real time
6. Host can click "Test" to start a multiplayer test round
