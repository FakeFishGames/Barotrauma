# User Messages & Feedback Log

## Session 11 Feedback

### Bug Reports

1. **Host password breaks connection** — Enabling password causes an error and prevents the hostclient from joining the server. The host shouldn't need to enter a password since they're the owner.

2. **Undo tab layout issues** — Undo tabs slightly hide behind the undo list. Selected tab buttons are less tall than unselected ones. The tab/undo buttons need to be inside the undo dropdown, and the dropdown list should change depending on the selected tab.

3. **Client crashes when placing items** — `StructurePrefab.UpdatePlacing` NullReferenceException at line 56 (`Submarine.MainSub.Position`). Root cause: `_CollaborativeSync.sub` was written as raw XML instead of GZip-compressed, causing `LoadSubmarineFromXml` to fail → `MainSub` becomes null.

4. **Host crashes when ending test mode** — `CircuitBox.IsCircuitBoxSelected(dummyCharacter)` NullReferenceException because `dummyCharacter` is null after returning from test. Also `Select()` crashes at `MainSub.UpdateTransform()` because `LoadSubmarineFromXml` failure left `MainSub` null.

5. **"Failed to send message to host: FailedNotConnected" spam** — `SendCursorPosition` keeps trying to send after the Lidgren connection drops but before `isActive` is set to false.

6. **_CollaborativeSync.sub format error** — "The archive entry was compressed using an unsupported compression method" — file was written with `SaveSafe()` (raw XML) but `.sub` files must be GZip-compressed via `SaveUtil.CompressStringToFile()`.

### Feature Requests / Suggestions

1. **Persist undo steps between tests** — Only clear undo history when closing the game, not when entering/exiting test mode.

2. **Skip end-round transition** — Remove the slow camera scroll when returning from test mode to editor.

3. **Server hosting improvements** — Server should work like standard multiplayer hosting:
   - Server browser support with "SubEditor" as a filter
   - Password protection for private sessions
   - Public/private toggle
   - Host should NOT need to enter password (authenticates via ownerKey)

4. **Per-user undo/redo system**:
   - Host has full global stack of ALL changes from ALL users
   - Clients keep their own undo stack (can only undo their own changes)
   - Host can see per-user tabs: [All] [Me] [User1] [User2]...
   - CAD-style selective undo: click to remove a specific command from the stack
   - When removing an "add item" step, cascade-remove dependent "changed item" steps

### Error Logs

**Client crash (StructurePrefab.UpdatePlacing):**
- Root cause: `_CollaborativeSync.sub` written as raw XML → `SubmarineInfo.OpenFile` fails → `LoadSub` fails → `MainSub` is null → crash when trying to place structure

**Host crash (CircuitBox.IsCircuitBoxSelected):**
- Root cause: `dummyCharacter` is null after test mode return → `character.SelectedItem` NullRef

**Host crash (Select → MainSub.UpdateTransform):**
- Root cause: `LoadSubmarineFromXml` failure (same _CollaborativeSync.sub issue) → `MainSub` null → crash at `MainSub.UpdateTransform()`

## Session 12 Feedback

### Bug Reports

1. **Undo steps don't disappear when undone** — After ctrl+z, the undo list still shows the undone step. It should be removed. Fixed: panel now only shows commands up to commandIndex.

2. **Duplicate undo entries from client** — When a client places something, identical steps appear in the list. Root cause: `StoreCommand` didn't check `isApplyingRemoteChange`, so remote changes created duplicate commands. Fixed: added guard in StoreCommand.

3. **Ctrl+Z deletes untouched objects** — Global undo list could be empty while client's wasn't (or vice versa), leading to undoing commands that reference stale entity IDs. Root cause: duplicate commands with wrong IDs + no validation. Fixed: duplicate prevention + stale command cleanup.

4. **Undo doesn't work after editing and returning from test** — Commands reference entity IDs that get re-assigned after submarine reload. Fixed: ValidateCommandsAfterReload removes stale commands.

5. **Host deleting steps doesn't delete for clients** — CAD-undo broadcasts entity changes but doesn't remove the command from client stacks. Current limitation: clients' local undo stacks are independent.

6. **Password breaks server** — Host was prompted for password when connecting to own server. Fixed: `ConnectToHostedServer` now sets `ClientPeer.AutomaticallyAttemptedPassword`.

### Mouseover highlight
- Undo list text should turn red on hover. Fixed: changed from GUITextBlock to GUIButton with HoverTextColor=Red.

## Session 13 Feedback

### Bug Reports

1. **Client tab not popping up** — Undo tabs only appeared when commands existed for a user, but tabs should show as soon as a client joins and persist after disconnect.

2. **Wiring mode undo not supported** — `ClearUndoBuffer()` was called in `SetMode()` every time mode switched, destroying all undo history. Wire changes bypassed the undo system entirely.

### Feature Requests

1. **Wire undo tracking** — Track all wire operations:
   - Adding wires (connecting two pins)
   - Removing wires (disconnecting)
   - Moving wire nodes
   - Adding wire nodes (Ctrl+Click)
   - Removing wire nodes (Right-Click)
   - Descriptions like "Item1 [pin_out] wired to Item2 [pin_in] using bluewire"

2. **Subtabs (Edits / Wires)** — Each user tab has two subtabs separating edit commands from wire commands.

3. **Physical "Undo Latest" button** — Button at bottom of undo list that undoes the most recent command from the currently selected tab and subtab.

4. **Persistent tabs** — Tabs for all connected users appear immediately on join and persist after disconnect (shown grayed with "(left)" suffix).

5. **CircuitBox wire undo** — Separate undo tab inside each CircuitBox UI (future feature, not implemented yet).

6. **Undo steps should persist** — Never cleared on mode switch, only on closing the game.

## Session 14 Feedback

### Bug Reports

1. **Wires deleted on mode switch** — Host switching between wiring and editing mode caused wires to be deleted. Root cause: `SetMode` called `SyncSubmarineToClients()` on mode switch, which was overwriting wire state. Fixed: removed the sync call since wire changes are now tracked via WireCommand.

2. **Client deleting steps doesn't work** — Client's CAD-undo not properly executing commands. Root cause under investigation: may be related to `isApplyingRemoteChange` guard or entity ID mismatch between client and host.

3. **Duplicate "Me" tab on host** — Host saw 4 tabs including a duplicate of "Me" with the host's name. Root cause: host's own session ID was being added to `undoTabUsers` AND "Me" was being created from it. Fixed: skip host session in undoTabUsers, create "Me" explicitly.

4. **Tabs underneath subtabs** — Layout overlap issue. Fixed: redesigned panel to horizontal split with tabs on left, content on right.

5. **Insufficient horizontal space** — Panel was too narrow. Fixed: tripled panel width (0.15 → 0.35).

6. **Host can't see client's steps** — Host tab filtering didn't match client session IDs.

7. **Wire undo leaves connected wires** — When undoing placement of an item that had wires connected, wires remained connected with no loose end to disconnect.

8. **LINKS NOT SYNCING** — Root cause found: `Item.Save()` writes `"linked"` attribute but `OnCollaborativeEntityUpdated` was looking for `"linkedto"`. Attribute name mismatch meant links were never parsed on receive. Fixed.

### Layout Changes
- Panel width: 0.15 → 0.35 (triple)
- User tabs: vertical, on LEFT side (25% width)
- Subtabs (Edits/Wires): horizontal, top of RIGHT side (75% width)
- Command list: below subtabs in right column
- Undo Latest button: bottom of right column

## Session 15 Feedback

### Bug Reports

1. **Wires not disconnected when addition steps cleared** — When removing a wire Connect command via CAD undo, the wire remained connected to pins with no loose ends. Root cause: `WireCommand.UnExecute()` called `conn.DisconnectWire(wire)` (removes wire from Connection's list) but NOT `wire.RemoveConnection(conn)` (which sets wire's internal connection reference to null). Wire still thought it was connected. Fixed: now calls both.

2. **Links STILL not syncing** — Link sync code was verified correct (entity.Save → linked attribute → NotifyEntityUpdated → receiver parses). Hull.cs had a bug: `linkedTo.Contains(this)` instead of `linkedTo.Contains(entity)` prevented adding links. 

3. **UI too tall, not wide enough** — Player tabs were evenly spaced with huge gaps due to `Stretch=true`. Fixed: `Stretch=false` with top-alignment and fixed 25px min height per tab.

4. **Host not getting undo steps from clients** — Root cause: `StoreCommand()` guard `if (isApplyingRemoteChange) return` was too broad. When `OnCollaborativeEntityPlaced` set `isApplyingRemoteChange=true` and then called `StoreCommand(cmd)` to add to host's stack, the guard rejected it. Fixed: allow commands with pre-set `AuthorSessionId` through the guard.

5. **Client undo UI whitespace** — Non-host clients showed empty tab column (25% width). Fixed: hide tab column and give full width to command list for non-host.

6. **Doubled wiring steps** — Initial one-sided connection and finalized double connection created separate commands. Fixed: `OnWireConnected` removes initial one-sided command when second end connects. Only keeps finalized connection.

7. **Join button and server browser** — "Join" button should open the server browser with SubEditor filter pre-checked, not just an IP input dialog. Already implemented in Session 14 via `GameMain.ServerListScreen`.

## Session 16 Feedback

### Bug Reports

1. **UI still too tall** — Panel too tall (500px min). Tab buttons shrink when selected. List truncates longer descriptions. Text not left-aligned. Fixed: panel min 350px, tabs fixed 28px with MaxSize, text left-aligned via constructor, list wider.

2. **Wire undo only disconnects end side** — When removing a wiring step, only one end was disconnected. The wire should be fully removed (both ends disconnected + entity deleted). Fixed: UnExecute now disconnects OtherConnection too and removes wire entity.

3. **Wired object movement far away on other clients** — Moving an item with wires only sent the item's position, not the wire's. On other clients, the wire endpoint stayed at old position. Fixed: UpdateCollaborativeEntityMoves now also sends position updates for wires connected via ConnectionPanel.

4. **Wires don't sync immediately** — Wire connections only synced on movement, not when first connected. Fixed: OnWireConnected/OnWireDisconnected now calls SyncWireAndConnectedItems for full entity state sync.

5. **Waypoint links don't sync** — WayPoint.Save() writes `linkedto0="id1"`, `linkedto1="id2"` (numbered attributes), but the parser only handled Item's `linked="id1,id2"` format. Fixed: parser now handles both formats. Also: waypoint ladder/gap association changes now sync via SyncLinkedEntityState.

6. **Join button still shows IP input** — Join button should open server browser with SubEditor filter. Fixed: ShowJoinSessionPrompt now calls GameMain.ServerListScreen.Select().

## Session 17 Feedback

### Bug Reports

1. **Link removal doesn't sync** — Removing a link (clearing all links) wasn't synced because `if (newLinkedIds.Count > 0)` prevented clearing. When XML has link data but it's empty, links should be cleared on receiver. Fixed: check if XML contains link data at all, then always rebuild (including empty).

2. **Wires don't sync immediately (wire connections)** — `OnCollaborativeEntityUpdated` only applied serializable properties but didn't process wire connection data (ConnectionPanel sub-elements with `<output>/<input><link w="..." i="..."/>` elements). Fixed: added wire connection parsing to `OnCollaborativeEntityUpdated` — connects/disconnects wires based on XML state.

3. **Wire node positions don't sync** — Wire node positions (stored in `nodes` attribute on Wire component element) weren't being applied on the receiver. Fixed: parse and apply `nodes` from Wire component sub-element in `OnCollaborativeEntityUpdated`.

4. **WayPoint ladder/gap/spawntype not syncing** — WayPoint properties like `SpawnType`, `Ladders`, `ConnectedGap` are NOT standard serializable properties (they're manual XAttributes in WayPoint.Save). Fixed: added WayPoint-specific handling in `OnCollaborativeEntityUpdated`.

5. **Join button not pre-filtering** — `ShowJoinSessionPrompt` opened ServerListScreen but without pre-selecting the SubEditor filter. Fixed: added `SetGameModeFilter("subeditor")` method to ServerListScreen, called before Select().

6. **Panel not wide enough** — Undo panel width increased from 0.42 to 0.5 relative width (MinSize 650px).

## Session 18 Feedback

### Bug Reports

1. **Links/waypoint linking not in undo** — Linking/unlinking entities via Space+Click had no undo steps. Fixed: new `LinkCommand` class tracks link creation/removal. Integrated into Item.cs, Hull.cs, WayPoint.cs linking code. Shows as "Linked [x] to [y]" / "Unlinked [x] from [y]".

2. **Panel bottom too far below list** — The undo panel was taller than its content. Fixed: reduced height from 0.45 to 0.35 relative. The list content fills the panel without excess space below.

3. **Wiring steps not synced between clients** — Wire undo steps stored locally but not forwarded to host's undo stack. Wire state changes DO sync via SyncWireAndConnectedItems (visual sync works), but individual WireCommand entries are per-client. Wire undo tracking is local.

4. **Container items teleporting on move** — Moving items with ConnectionPanels (batteries, fabricators, cabinets, lamps) caused them to teleport on other clients. Root cause: UpdateCollaborativeEntityMoves added connected Wire items to the sync set and sent their positions via EntityMoved. Wire Items' rect positions don't correspond to their visual location (determined by nodes). Fixed: skip Wire items in movement sync.

5. **Server browser filter title blank** — SubEditor GameModePreset's localization key "GameMode.subeditor" doesn't exist. Fixed: added fallback display name from identifier (capitalizing first letter).

6. **Wire sync corrupts other items** — Entity updates for property changes (like color) included stale ConnectionPanel XML data. When the receiver processed this, it overwrote the correct wire state. Fixed: removed ConnectionPanel XML processing from OnCollaborativeEntityUpdated. Wire connections synced separately via SyncWireAndConnectedItems.

7. **Links rendered super thin** — Link lines between entities used fixed 1px width, becoming invisible when zoomed out. Fixed: minimum screen-space width of 2-5px based on zoom level (similar to hull outline rendering).

8. **Hulls fade in slowly** — Hull opacity used a gradual alpha interpolation (3-second fade-in) after ambient light edits. In SubEditor mode, hulls should appear at full opacity immediately. Fixed: skip alpha fade when Screen.Selected is SubEditorScreen.

## Session 19 Feedback

### Bug Reports (from user)

1. **List still too short** — Panel MinSize was only 250px, and the list took 86% of that (193px effective). Fixed: Panel MinSize 250→500px, panel height 0.35→0.55, list relative Y 0.86→0.88.

2. **Wiring still not syncing** — Root cause discovered: `SyncWireAndConnectedItems` sent entity XML via `NotifyEntityUpdated`, but `OnCollaborativeEntityUpdated` had been modified in Session 18 to SKIP ConnectionPanel processing (to prevent corruption from property-only updates). Wire connections were NEVER applied on receiver. Fixed: added `wireSync="true"` flag to entity XML sent from wire sync. `OnCollaborativeEntityUpdated` only processes ConnectionPanel data when this flag is present.

3. **Items still teleporting (junction boxes, cabinets)** — Root cause discovered: `WorldPosition` includes `Submarine.HiddenSubPosition` which is calculated LOCALLY per-client and DIFFERS between host and client (based on submarine load order, level data size). Movement sync was sending/receiving WorldPosition-based coordinates. The delta calculation was wrong because both sides had different submarine offsets. Fixed: send `entity.Rect.X/Y` (submarine-local coordinates) instead of `WorldPosition`. Receiver computes delta from its own `Rect` position, which is consistent regardless of HiddenSubPosition.

4. **Links still get thinner when zooming out until they disappear** — Previous fix was in wrong method (DrawLinkedTo doesn't exist). Actual link drawing is in Hull.cs (width: 2px), WayPoint.cs (width: 5px), and LinkedSubmarine.cs (width: 3px). These are fixed pixel widths that become invisible at zoom. Fixed: scale with zoom using `Math.Max(baseWidth, scale/cam.Zoom)` in the actual drawing methods.

## Session 21 Feedback

### Bug Reports (from user)

1. **List too short / window too tall** — User wants the list to be LONGER, not the window. Previous sessions kept making the window taller instead of making the list fill more of it. Fixed: subtabs 7%→4%, button 7%→4%, list 88%→92%.

2. **Wiring still not syncing** — Wire entities were not being PLACED on the receiver before trying to connect them. In wiring mode, wires are created by `ConnectionPanel.ApplyWirePanelChanges()` which doesn't call `StoreCommand`/`NotifyEntityPlaced`. The receiver didn't have the wire entity, so `Entity.FindEntityByID(wireId)` returned null. Fixed: `SyncWireAndConnectedItems` now sends wire Item as `NotifyEntityPlaced` FIRST, then sends wireSync updates.

3. **Items still teleporting** — ROOT CAUSE FOUND: Entity IDs were being REMAPPED during submarine sync. `LoadSubmarineFromXml` called `LoadSub → new Submarine(subInfo)` which uses `IdRemap.DetermineNewOffset()` to assign new entity IDs. Host had IDs [1001, 1002, ...] but client had [1, 2, ...]. ALL EntityMoved/EntityUpdated messages targeted WRONG entities. Fixed: `LoadSubmarineFromXml` now uses `loadEntities` callback with `IdRemap(null, 0)` to preserve sender's entity IDs exactly.

4. **Movement sync also used absolute positions** — Even with matching IDs, absolute rect positions could differ after worldX/worldY correction during entity placement. Fixed: movement sync now sends DELTAS (currentPos - lastTrackedPos) instead of absolute positions.

5. **Sub file loading "received empty sub XML"** — Related to same ID remapping issue. The entities loaded from file on the host had IDs in one range, and the sync XML was sent with those IDs. The client loaded them with remapped IDs, then subsequent messages targeted wrong entities. Fixed by preserving IDs.

## Session 23 Feedback

### Bug Reports

1. **Wire placements sync, but node changes don't** — Adding, removing, and moving wire nodes did not sync. Wire node callbacks (`OnSubEditorNodeMoved/Added/Removed`) created local `WireCommand` entries but never sent node changes to other clients. Fixed: Wire node changes now call `NotifyEntityUpdated` on the wire's parent Item.

2. **Wiring undo subtab doesn't sync to host** — Host sees wire as "edit" (added wire) but not as "wiring change" (connected x to x). The wiring subtab was blank. Fixed: New `WireUndoStep` packet. Clients send wire undo step descriptions to server, relayed to host, stored in host's undo stack.

3. **CRITICAL: Items teleport on drag-and-drop** — ROOT CAUSE: HiddenSubPosition DIFFERS between host and client (depends on number of loaded subs, level data, sub iteration alternation). entity.Rect.X/Y includes HiddenSubPosition, so sending absolute Rect values to a receiver with different HiddenSubPosition computed wrong deltas. Arrow keys "worked" because after the first teleport, subsequent 1px moves were correct from the corrected position. Fixed: Movement sync now sends DELTAS, not absolute positions.

4. **Entity corruption (rect, color, scale)** — `OnCollaborativeEntityUpdated` was applying rect from entity XML which included stale HiddenSubPosition-offset coordinates, corrupting dimensions. Fixed: Skip rect application in entity updates (movement handled by EntityMoved).

5. **Wire rendering missing** — Wire entities existed but weren't drawn. `Wire.Load()` adds nodes but does NOT call `UpdateSections()` which creates the renderable sections. Fixed: `wire.UpdateSections()` called after wireSync connections and entity placement.
