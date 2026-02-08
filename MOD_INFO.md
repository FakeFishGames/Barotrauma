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

### Shared (Client + Server)

| File | Change |
|------|--------|
| `Networking/SubEditorNetworking.cs` | Added packet headers for the new message types: `SubmarineInfo`, `ReturnToEditor`, `EntityPlaced/Removed/Moved`, `EntityPropertyChanged`, `CursorMoved`, `EntityUpdated`, `FullState`, etc. |
| `Networking/NetworkMember.cs` | Added `SUBEDITOR` entry to `ClientNetObject` and `ServerNetObject` enums (done in upstream PR#2 merge). |
| `GameSession/GameModes/GameModePreset.cs` | Added `SubEditor` game mode preset for multiplayer SubEditor sessions. |
| `Map/Levels/Level.cs` | Added `IsSubEditorTestMode` flag. When set, level generation skips caves, wrecks, ruins, corpses, outposts, and other content so test rounds start fast and clean. |
| `Events/EventManager.cs` | Added a fallback for SubEditor/Sandbox mode when no level is set, preventing a crash during event manager initialization. |
| `Characters/Animation/Ragdoll.cs` | Defensive null-checks on `limb.body` and `Collider` to prevent crashes when characters have incomplete physics (can occur during SubEditor test mode cleanup). |

### Client

| File | Change |
|------|--------|
| `Networking/SubEditorNetworkingClient.cs` | Extended with methods for sending/receiving entity sync, cursor updates, test mode, submarine file handling, and session management. The client-side networking hub for all collaborative features. |
| `Screens/SubEditorScreen.cs` | Added Host/Join buttons, collaborative user panel (collapsible), cursor drawing for remote users, entity/property/transform sync hooks in undo/redo and command store, test mode entry/exit with XML snapshot preservation, chat box integration, and position tracking for real-time move sync. |
| `Networking/GameClient.cs` | Handles `SubEditorPacketHeader` messages from the server (entity placed/removed/moved, property changes, cursor moves, submarine info, test mode start/end). Modified round-start flow to support SubEditor test mode (skip sub hash checks, use in-memory sub). |
| `Networking/Client.cs` | In SubEditor mode, positions voice audio relative to cursor positions instead of character positions for spatial audio in the editor. |
| `Networking/Voip/VoipClient.cs` | In SubEditor mode, disables radio/muffle filters and sets generous audio range so voice is always audible. |
| `Sounds/VoipSound.cs` | Added `SetRelativePosition()` method for stereo panning of voice audio relative to the listener. |
| `SubEditorCommands.cs` | Added public accessors (`AffectedEntities`, `IsDeleteOperation`, `ChangedPropertyName`, `SanitizedPropertyValue`) to `TransformCommand`, `AddOrDeleteCommand`, and `PropertyCommand` so the collaborative sync can read command data. |
| `GUI/GUI.cs` | Added "Return to Editor" button in the pause menu when in SubEditor collaborative test mode. |
| `GUI/ChatBox.cs` | Null-safe access to `CrewManager.ReportButtonFrame` (may not exist in SubEditor mode). |
| `Screens/NetLobbyScreen.cs` | Null-check on `traitorDangerGroup` (may not be initialized when lobby is used in SubEditor flow). |

### Server

| File | Change |
|------|--------|
| `Networking/SubEditorNetworkingServer.cs` | Extended with session management (start/add/remove clients), entity relay (placed/removed/moved/property/updated), cursor relay with authoritative session ID correction, submarine file transfer, test mode start/return flow, and console commands. |
| `Networking/GameServer.cs` | On client connect in SubEditor mode: starts session or adds to existing one, grants host permissions. Added `SubEditor` game mode handling in `TryStartGame`. Modified `EndGame` to return to SubEditor instead of lobby. Null-safe content preloading and level equality checks. |
| `GameMain.cs` | `IsSubEditorMode` changed to `internal set`. Added `-requireauthentication` command-line processing before server start (needed for LAN testing without Steam). Enables cheats in SubEditor mode. |
| `DebugConsole.cs` | Added server console commands: `subeditor_status`, `subeditor_sethost`, `subeditor_starttest`, `subeditor_endtest`. |

## Testing

1. Build server: `dotnet build Barotrauma/BarotraumaServer/LinuxServer.csproj -c Release`
2. Build client: `dotnet build Barotrauma/BarotraumaClient/LinuxClient.csproj -c Release`
3. Run client, open Submarine Editor, click "Host" to start a session
4. Run second client, open Submarine Editor, click "Join" with the host's address
5. Both editors should see each other's cursors and entity changes in real time
6. Host can click "Test" to start a multiplayer test round
