# Collaborative SubEditor Features

Status of planned improvements for the multiplayer submarine editor.

---

## ✅ 1. Unsaved Changes Prompt on All Exit Points

**IMPLEMENTED** — Both Host and Join buttons now check for unsaved changes (non-empty Commands list) before proceeding, showing the same confirmation dialog as the Back button.

---

## ✅ 2. Server Browser Visibility

**IMPLEMENTED** — SubEditor servers now register with the master server when "Public" is enabled in the host dialog. The server reports `GameModeIdentifier = "subeditor"` so the server browser filter works correctly. The game mode stays "subeditor" even during test mode.

---

## ✅ 3. Server Description Field

**IMPLEMENTED** — The host dialog now includes a "Description (optional)" text box. The description is passed to the server via the `-servermessage` command-line argument and stored in `ServerSettings.ServerMessageText`.

---

## ✅ 4. Host Button Race Condition

**IMPLEMENTED** — The `ConnectToHostedServer` coroutine now shows a modal "Connecting..." overlay that blocks all UI interaction until the client connects to the server. This prevents "fail to authenticate" errors from clicking UI elements before the connection is established.

---

## ✅ 5. Player Management in Editor

**IMPLEMENTED** — The Editors panel now shows Kick and Ban buttons for the host next to each non-host user. Uses the existing `GameClient.KickPlayer`/`GameClient.BanPlayer` infrastructure.

---

## ✅ 6. SubEditor Permissions System

**IMPLEMENTED** — New `SubEditorPermissions` flags enum in `SubEditorNetworking.cs`:

- **CanWireOwnInEditor** — Can add wires to own objects, edit/delete own wires
- **CanWireOthersInEditor** — Can edit others' wires and wire others' devices
- **CanEditOwn** — Can place and edit own objects
- **CanDeleteOwn** — Can delete own objects
- **CanEditOthers** — Can edit others' objects
- **CanDeleteOthers** — Can delete others' objects
- **CanManageOthers** — Can assign permissions, view/edit others' undo lists
- **CanUndoSelf** — Can undo own actions
- **CanMassEdit** — Can perform mass edits (select-all + delete, etc.)

Host always has `All` permissions. Clients default to `None`. Permission checking methods integrated into `SubEditorNetworkingShared` with ownership-aware `CanUserEditEntity`/`CanUserDeleteEntity`.

**TODO**: Wire up UI for assigning permissions (settings dialog). Enforce permissions at the action level (currently data model only).

---

## ✅ 7. Object Ownership / Blame Tracking

**IMPLEMENTED** — `EntityOwnership` dictionary maps entity IDs to account identifiers (not usernames). Ownership is set when entities are placed and removed when deleted. Used by permission checking methods.

**TODO**: Persist blame data to file alongside the .sub file. Display ownership information in entity tooltips.

---

## ✅ 8. Undo Steps as Server Log

**IMPLEMENTED** — New Activity Log panel with toggle button below the Editors panel. Shows timestamped entries for every command with author name and color. Auto-scrolls to newest entries.

---

## ✅ 9. Persistent Undo History

**IMPLEMENTED** — Commands are saved to a `.undohistory` text file alongside the `.sub` file on every save. When opening a submarine, the history is loaded and displayed in the Activity Log. Includes a "Clear History" button with confirmation prompt.

The history format is simple text lines: `[timestamp] [author] description`. New commands are appended on each save. File size should remain reasonable since each entry is a single line describing the action.
