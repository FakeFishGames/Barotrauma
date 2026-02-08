# Collaborative SubEditor Cleanup TODO

## AI-Fabricated Artifacts (not in upstream master OR dev)
- [x] Remove `TimeTrialMission.cs` (shared) - fabricated class, doesn't exist anywhere in real game
- [x] Remove `TimeTrialMission.cs` (client) - fabricated class, doesn't exist anywhere in real game
- [x] Revert `MissionPrefab.cs` to upstream - had `TimeTrial` entry for fabricated class
- [x] Revert `MainMenuScreen.cs` to upstream - had "COPILOT BUILD TEST" hardcoded instead of `TextManager.Get("QuitButton")`
- [x] Revert `TestGameMode.cs` to upstream - had unused `IsCollaborativeTest` property (declared, never read)

## "Just In Case" Null-Checks (AI shotgun debugging leftovers)
These are places where the AI hit a crash during trial-and-error, and instead of fixing the
root cause in the SubEditor flow, plastered null-checks on existing upstream code.

- [x] `GameServer.cs WriteRoundStartFinalize` (line ~3374): `GameMain.GameSession?.EventManager?.GetFilesToPreload()`
  - Upstream code: `GameMain.GameSession.EventManager.GetFilesToPreload()` (no nulls)
  - WHY AI changed it: SubEditor test mode calls TryStartGame->WriteRoundStartFinalize but EventManager may not be fully initialized
  - REAL FIX: Reverted to upstream. SubEditor test mode uses Sandbox mode which sets up EventManager properly.

- [x] `GameServer.cs WriteRoundStartFinalize` (line ~3392): `GameMain.GameSession?.Level?.EqualityCheckValues[stage] ?? 0`
  - Upstream code: `GameMain.GameSession.Level.EqualityCheckValues[stage]`
  - WHY AI changed it: SubEditor test mode might not have a level
  - REAL FIX: Reverted to upstream. SubEditor test mode uses TryStartGame which creates a level.

- [x] `GameServer.cs EndGame` (line ~3417): `missions ??= GameMain.GameSession?.Missions?.ToList() ?? Enumerable.Empty<Mission>()`
  - Upstream code: `missions ??= GameMain.GameSession.Missions.ToList()`
  - WHY AI changed it: SubEditor test mode EndGame might have null GameSession
  - REAL FIX: Reverted to upstream. EndGame is only called when GameStarted is true, which means GameSession exists.

- [x] `GameServer.cs EndGame` (line ~3445): try-catch around `KarmaManager.OnRoundEnded()`
  - Upstream code: bare `KarmaManager.OnRoundEnded()` call
  - WHY AI changed it: KarmaManager crashed during SubEditor round end
  - REAL FIX: Reverted to upstream. KarmaManager is always valid when GameStarted is true.

- [x] `Ragdoll.cs` (line ~375-377): `if (limb?.body != null)` and `if (Collider != null)`
  - Upstream code: `limb.body.Submarine = currSubmarine;` and `Collider.Submarine = currSubmarine;`
  - WHY AI changed it: Probably crashed during SubEditor test mode character cleanup
  - REAL FIX: Reverted to upstream. These should never be null in valid game state. If they crash, it's a SubEditor cleanup bug to fix there.

- [x] `NetLobbyScreen.cs` (line ~2953): `if (traitorDangerGroup == null) { return; }`
  - Upstream code: no null check (traitorDangerGroup always initialized in constructor)
  - WHY AI changed it: Lobby screen accessed before full initialization during SubEditor flow
  - REAL FIX: Reverted to upstream. The SubEditor EndGame path already skips NetLobbyScreen.Select().

## Legitimate Null-Checks (keep these)
- [x] `ChatBox.cs` (line ~628): `GameMain.GameSession?.CrewManager?.ReportButtonFrame?.Rect.Width ?? 0`
  - WHY: SubEditor mode uses ChatBox (for collaborative chat) but has no GameSession/CrewManager
  - This is correct - gracefully degrades popupMessageOffset to 0 when no report button exists

## Anti-Patterns in Mod Code
- [x] `GameMain.cs`: Double CLI loop for `-requireauthentication` (processed in separate loop before StartServer, then skipped in post-start loop with "Already processed above" comment)
  - REAL FIX: Moved `-requireauthentication` into the post-start loop alongside `-playstyle`, `-karma`, etc. Removed the separate loop and the dummy "Already processed above" case.

- [x] `SubEditorNetworkingServer.cs StartSubEditorTestRound`: Giant try-catch wrapping entire method body
  - Indentation was broken (try block not indented properly)
  - Multiple nested try-catches
  - Excessive debug logging ("Pre-TryStartGame: GameStarted=... initiated=...")
  - REAL FIX: Rewrote with proper structure. Only try-catch around SubmarineInfo loading (external file). Removed debug state dumps. Fixed indentation.

## Dead Code
- [x] `VoipSound.cs SetRelativePosition()` method - defined but NEVER called from anywhere
  - The Client.cs voice positioning uses `SetPosition()` and `SetRange()` instead
  - REAL FIX: Removed it

## Files That Need Review For Sloppy Patterns
- [x] `SubEditorNetworkingServer.cs` - Cleaned up noisy debug logging (removed XML length dumps)
- [x] `SubEditorNetworkingClient.cs` - Reviewed, follows patterns correctly
- [x] `SubEditorScreen.cs` - Reviewed collaborative additions, clean
- [x] `GameClient.cs` - Fixed remaining shotgun null-check (`EventManager?.PreloadContent`), rest is clean
- [x] `Client.cs` - Reviewed voice positioning, follows existing if/else pattern
- [x] `VoipClient.cs` - Reviewed radio filter skip, follows existing if/else pattern
- [x] `GUI.cs` - Reviewed pause menu button, follows existing pattern
- [x] `SubEditorCommands.cs` - Reviewed accessors, clean read-only wrappers
- [x] `DebugConsole.cs` (server) - Reviewed commands, follows existing patterns
- [x] `Level.cs` - Reviewed IsSubEditorTestMode, acceptable pattern for mod
- [x] `ChatBox.cs` - Kept null-check (legitimate: SubEditor has ChatBox but no CrewManager)
- [x] `EventManager.cs` - Reverted to upstream (SubEditor test mode creates a real level via TryStartGame, so fallback is unnecessary)

## Build Verification
- [x] Build server after all changes: 0 errors
- [x] Build client after all changes: 0 errors

## Documentation
- [x] Update MOD_INFO.md to match final state
