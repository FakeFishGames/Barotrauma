# Collaborative SubEditor Status

## Completed Cleanup (Sessions 1-2)

- [x] Remove `TimeTrialMission.cs` (shared + client) — fabricated class
- [x] Revert `MissionPrefab.cs` to upstream — had `TimeTrial` entry
- [x] Revert `MainMenuScreen.cs` to upstream — had "COPILOT BUILD TEST"
- [x] Revert `TestGameMode.cs` to upstream — unused `IsCollaborativeTest`
- [x] Revert shotgun null-checks (GameServer.cs, Ragdoll.cs, NetLobbyScreen.cs, EventManager.cs, GameClient.cs)
- [x] Fix double CLI loop in GameMain.cs for `-requireauthentication`
- [x] Remove dead code `VoipSound.SetRelativePosition()`
- [x] Fix indentation in `SubEditorNetworkingServer.cs StartSubEditorTestRound`
- [x] Keep legitimate null-check in `ChatBox.cs` (SubEditor has no CrewManager)

## Completed Bug Fixes (Sessions 3-14)

- [x] Test mode return syncs for non-host clients (preTestSubmarineXml snapshot)
- [x] Chat box positioning (Anchor.BottomLeft)
- [x] Session leave preserves submarine state
- [x] Entity rect corruption (4x tiling) — removed rect application from OnCollaborativeEntityUpdated
- [x] Undo/redo syncs property and transform changes
- [x] Disconnect reflected in collaborative UI
- [x] EnterSubEditor vs EndGame race condition
- [x] SyncSubmarine blocked during EndGame
- [x] Level.IsSubEditorTestMode cleared after test mode
- [x] Infinite sync loop (LoadSub auto-sync suppressed via suppressLoadSubAutoSync)
- [x] Legacy file-transfer auto-loading removed (XML sync is canonical)
- [x] GameModeIcon.subeditor GUI style error (filter non-votable modes)
- [x] Host/Join dialog sizing
- [x] StartTestMode packet sent to clients before TryStartGame
- [x] _CollaborativeSync.sub uses GZip compression (SaveUtil.CompressStringToFile)
- [x] dummyCharacter null checks before CircuitBox.IsCircuitBoxSelected
- [x] MainSub null safety fallback in Select()
- [x] SendCursorPosition peer connection guard
- [x] Entity placement sync (worldX/worldY absolute coordinates)
- [x] Link sync (attribute name: "linked" not "linkedto")
- [x] Password hosting (AutomaticallyAttemptedPassword for host)
- [x] Skip end-round camera transition for SubEditor
- [x] Undo persists across test mode transitions
- [x] Console popup suppressed (ThrowError → AddWarning for SubEditor-specific errors)

## Completed Features (Sessions 8-14)

- [x] Per-user undo stacks with AuthorSessionId
- [x] Per-user undo tabs (host sees all users, clients see only own)
- [x] Undo subtabs (Edits / Wires)
- [x] CAD-style selective undo with dependency cascade
- [x] Undo Latest button
- [x] WireCommand class (connect, disconnect, node move/add/remove)
- [x] Wire change capture callbacks (Connection.cs, Wire.cs)
- [x] ClearUndoBuffer removed from SetMode (undo persists across Default↔Wiring)
- [x] Persistent user tabs (appear on join, persist after disconnect)
- [x] Server hosting with password, public/private toggle, max players
- [x] GetAffectedEntityIds() on all Command types

## Remaining / Known Issues

- [ ] Client deleting steps may not always work (CAD undo from panel)
- [ ] Wire undo may leave connected wires if target item was deleted via separate command
- [ ] CircuitBox wire undo (separate undo tab inside CircuitBox UI) — future feature
- [ ] Mod distribution as a standalone package (runtime C# injection) — future architectural task
- [ ] Some edge cases with wires being deleted on mode switch (mitigated by removing SyncSubmarineToClients from SetMode)

## Build Verification

- [x] Server: 0 errors
- [x] Client: 0 errors
