# Collaborative SubEditor Features

Status of planned improvements for the multiplayer submarine editor.

---

## ✅ 1. Unsaved Changes Prompt on All Exit Points

**IMPLEMENTED** — Both Host and Join buttons now check for unsaved changes (non-empty Commands list) before proceeding, showing the same confirmation dialog as the Back button.

---

## ⚠️ 2. Server Browser Visibility

**PARTIALLY IMPLEMENTED** — SubEditor servers set `GameModeIdentifier = "subeditor"` before server registration. The game mode stays "subeditor" even during test mode.

**KNOWN ISSUE**: Making the server public still fails to authenticate. This appears to be an EOS/Steam authentication issue that may require deeper investigation into the server registration flow.

---

## ✅ 3. Server Description Field

**IMPLEMENTED** — The host dialog includes a "Description (optional)" text box. Passed to the server via `-servermessage` argument → `ServerSettings.ServerMessageText`.

---

## ✅ 4. Host Button Race Condition

**IMPLEMENTED** — Modal "Connecting..." overlay blocks all UI interaction until the client connects to the server.

---

## ✅ 5. Player Management in Editor

**IMPLEMENTED** — Right-click on users in the Editors panel opens the same context menu as multiplayer (kick, ban, permissions, rank, mute, view profile) via `NetLobbyScreen.CreateModerationContextMenu()`. Server Settings button opens the existing `ServerSettings.ToggleSettingsFrame()` with all tabs (ServerIdentity, General, Antigriefing, Banlist).

---

## ✅ 6. SubEditor Permissions System

**IMPLEMENTED** — `SubEditorPermissions` flags enum in `SubEditorNetworking.cs`:

- **CanWireOwnInEditor** — Can add wires to own objects, edit/delete own wires
- **CanWireOthersInEditor** — Can edit others' wires and wire others' devices
- **CanEditOwn** — Can place and edit own objects
- **CanDeleteOwn** — Can delete own objects
- **CanEditOthers** — Can edit others' objects
- **CanDeleteOthers** — Can delete others' objects
- **CanManageOthers** — Can assign permissions, view/edit others' undo lists
- **CanUndoSelf** — Can undo own actions
- **CanMassEdit** — Can perform mass edits (select-all + delete, etc.)

Host always has `All` permissions. Clients default to `None`. Permission checking via `CanUserEditEntity`/`CanUserDeleteEntity`.

**TODO**: Enforce permissions at the action level (currently data model only).

---

## ✅ 7. Object Ownership / Blame Tracking

**IMPLEMENTED** — `EntityOwnership` dictionary maps entity IDs to account identifiers (not usernames). Set on entity placement, removed on deletion.

**TODO**: Persist blame data to file. Display ownership in entity tooltips.

---

## ✅ 8. Undo Steps as Activity Log

**IMPLEMENTED** — Activity Log panel created at startup (works offline too). Toggle button always visible in top-right. Shows timestamped entries for every command with author name and color. Auto-scrolls to newest.

---

## ✅ 9. Persistent Undo History

**IMPLEMENTED** — Commands saved to `.undohistory` text file alongside `.sub` file on every save. Loaded and displayed in Activity Log when opening a submarine (works offline). "Clear History" button with confirmation prompt. Format: `[timestamp] [author] description` — one line per action.
