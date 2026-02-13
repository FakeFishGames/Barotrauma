# Future Collaborative SubEditor Features

Ideas and planned improvements for the multiplayer submarine editor.

---

## Unsaved Changes Prompt on All Exit Points

Currently, clicking the back arrow prompts "are you sure?" if there are unsaved changes. However, clicking "Join" (which boots you to the server browser) or other exit paths do not prompt. All exit points from the SubEditor need to check for unsaved changes and prompt the user before leaving.

---

## Server Browser Visibility

The collaborative SubEditor server does not appear correctly in the server browser. Even when set to public, it doesn't show up. If you favorite the server, it always shows as offline — but clicking it still connects you. The server needs to:

- Appear in the server browser when public is enabled.
- Use the SubEditor game mode filter, regardless of whether the host is in editor mode or test mode.
- Show accurate online/offline status.

---

## Server Description Field

Currently there is only a spot for the server name. Add a description field as well, so hosts can provide context about what they're building or any rules.

---

## Host Button Race Condition

If you click "Host" and then immediately interact with UI elements (like the OK button) before the client has fully joined, a "fail to authenticate" error occurs. The UI should either disable interactive elements until the connection is fully established, or gracefully handle early interactions.

---

## Player Management in Editor

The multiplayer player menu (permissions, kick, ban) already exists in test mode. It needs to be integrated into the editor's "Editors" player list panel on the right side. This would allow the host to manage connected players without switching to test mode.

---

## SubEditor Permissions System

The current permissions are hacked together. A proper permission system is needed:

### General Rules
1. Cheats should be enabled by default in both editor and test mode.
2. Clients (non-host) should have no permissions by default.
3. A new permission category specific to SubEditor and multiplayer test mode is needed.
4. There should be a way to configure default permissions, similar to the "Server Settings" button in the multiplayer lobby (only the relevant tabs: "Server", "Banlist", and wherever "Default Permissions" lives).

### Proposed Permissions
- **CanWireOwnInEditor** — Can add wires to their own objects, and edit/delete their own wires. (Distinct from wiring in test mode, which doesn't persist.)
- **CanWireOthersInEditor** — Can edit other players' wires and wire other players' devices.
- **CanEditOwn** — Can place and edit their own objects.
- **CanDeleteOwn** — Can delete their own objects.
- **CanEditOthers** — Can edit other players' objects.
- **CanDeleteOthers** — Can delete other players' objects.
- **CanManageOthers** — Can assign SubEditor permissions to other players and view/edit others' undo lists (requires higher permission level than the target). Could reuse the existing player management permission infrastructure.
- **CanUndoSelf** — Can undo their own actions.
- **CanMassEdit** — Can perform large-scale edits (e.g., select-all + delete). If denied, the action is blocked entirely.
- **MassEditThreshold** — The number of items that must be affected at once for an action to count as a "mass edit."

---

## Object Ownership / Blame Tracking

Currently, ownership is tracked via undo steps (who performed the action). A more robust system is needed:

- Associate each object ID with a player ID (not username, since usernames can change).
- Persist blame data across sessions.
- Use this for permission checks (e.g., "can this player edit this object?").

---

## Undo Steps as Server Log

Repurpose the undo step history as a server activity log. Copy the text into a dedicated server log panel so the host can review all actions taken by all players.

---

## Persistent Undo History

Consider saving undo history permanently on the host, associated with (but separate from) the .sub file. This would allow reviewing the full edit history of a submarine across sessions.

### Concerns
- File size could grow large over time — needs smart storage (e.g., delta compression, periodic compaction).
- Add a "Clear Submarine's Undo History" button with a confirmation prompt before deleting the history file.
