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

- [ ] `GameServer.cs WriteRoundStartFinalize` (line ~3374): `GameMain.GameSession?.EventManager?.GetFilesToPreload()`
  - Upstream code: `GameMain.GameSession.EventManager.GetFilesToPreload()` (no nulls)
  - WHY AI changed it: SubEditor test mode calls TryStartGame->WriteRoundStartFinalize but EventManager may not be fully initialized
  - REAL FIX: Revert to upstream. The SubEditor test mode now uses Sandbox mode which sets up EventManager properly. If it still crashes, fix the SubEditor setup, not upstream code.

- [ ] `GameServer.cs WriteRoundStartFinalize` (line ~3392): `GameMain.GameSession?.Level?.EqualityCheckValues[stage] ?? 0`
  - Upstream code: `GameMain.GameSession.Level.EqualityCheckValues[stage]`
  - WHY AI changed it: SubEditor test mode might not have a level
  - REAL FIX: Revert to upstream. SubEditor test mode uses TryStartGame which creates a level. If Level is null, the SubEditor setup is broken.

- [ ] `GameServer.cs EndGame` (line ~3417): `missions ??= GameMain.GameSession?.Missions?.ToList() ?? Enumerable.Empty<Mission>()`
  - Upstream code: `missions ??= GameMain.GameSession.Missions.ToList()`
  - WHY AI changed it: SubEditor test mode EndGame might have null GameSession
  - REAL FIX: Revert to upstream. EndGame is only called when GameStarted is true, which means GameSession exists.

- [ ] `GameServer.cs EndGame` (line ~3445): try-catch around `KarmaManager.OnRoundEnded()`
  - Upstream code: bare `KarmaManager.OnRoundEnded()` call
  - WHY AI changed it: KarmaManager crashed during SubEditor round end
  - REAL FIX: Revert to upstream. Conditionally skip KarmaManager in SubEditor mode is handled by the SubEditor EndGame path which doesn't call the normal EndGame.

- [ ] `Ragdoll.cs` (line ~375-377): `if (limb?.body != null)` and `if (Collider != null)`
  - Upstream code: `limb.body.Submarine = currSubmarine;` and `Collider.Submarine = currSubmarine;`
  - WHY AI changed it: Probably crashed during SubEditor test mode character cleanup
  - NEEDS INVESTIGATION: Check if SubEditor test mode actually triggers this. If so, fix SubEditor cleanup, not Ragdoll.

- [ ] `NetLobbyScreen.cs` (line ~2953): `if (traitorDangerGroup == null) { return; }`
  - Upstream code: no null check (traitorDangerGroup always initialized in constructor)
  - WHY AI changed it: Probably lobby screen accessed before full initialization during SubEditor flow
  - NEEDS INVESTIGATION: Check if SubEditor mode actually triggers SetTraitorDangerIndicators with null group. The EndGame code already skips NetLobbyScreen.Select() in SubEditor mode, so this may be a leftover from before that fix.

## Legitimate Null-Checks (keep these)
- [x] `ChatBox.cs` (line ~628): `GameMain.GameSession?.CrewManager?.ReportButtonFrame?.Rect.Width ?? 0`
  - WHY: SubEditor mode uses ChatBox (for collaborative chat) but has no GameSession/CrewManager
  - This is correct - gracefully degrades popupMessageOffset to 0 when no report button exists

## Anti-Patterns in Mod Code
- [ ] `GameMain.cs`: Double CLI loop for `-requireauthentication` (processed in separate loop before StartServer, then skipped in post-start loop with "Already processed above" comment)
  - REAL FIX: Move `-requireauthentication` into the post-start loop alongside `-playstyle`, `-karma`, etc. It modifies `Server.ServerSettings` which requires Server to exist. No need for separate loop.

- [ ] `SubEditorNetworkingServer.cs StartSubEditorTestRound`: Giant try-catch wrapping entire method body
  - Indentation is broken (try block not indented properly)
  - Multiple nested try-catches
  - Excessive debug logging ("Pre-TryStartGame: GameStarted=... initiated=...")
  - REAL FIX: Clean up indentation, remove debug logging, keep only essential error handling

## Dead Code
- [ ] `VoipSound.cs SetRelativePosition()` method - defined but NEVER called from anywhere
  - The Client.cs voice positioning uses `SetPosition()` and `SetRange()` instead
  - REAL FIX: Remove it

## Files That Need Review For Sloppy Patterns
- [ ] `SubEditorNetworkingServer.cs` - Full review for repetitive message-building patterns
- [ ] `SubEditorNetworkingClient.cs` - Full review
- [ ] `SubEditorScreen.cs` - Full review of collaborative additions
- [ ] `GameClient.cs` - Review SubEditor-specific if-checks scattered in StartGame flow

## Build Verification
- [ ] Build server after all changes
- [ ] Build client after all changes

## Documentation
- [ ] Update MOD_INFO.md to match final state
