# Cleanup Log

## Session 1 (previous agent)
- Merged master into branch
- Applied code from copilot/fix-collaborative-editing-bugs (most complete branch)
- Excluded AI handoff docs (COLLABORATIVE_SUBEDITOR_DEV_LOG.md, SUBEDITOR_COLLABORATIVE_TESTING.md)
- Removed some verbose comments
- Created MOD_INFO.md
- Reverted LidgrenClientPeer.cs (comment-only changes)

## Session 2 (current)

### Analysis findings
The previous cleanup was superficial - only removed verbose comments. The real problems are:

1. **AI-fabricated files**: TimeTrialMission.cs doesn't exist in upstream master OR dev. The AI made it up
   and added it to MissionPrefab.cs. This was brought into origin/master via PR#2 merge.

2. **"COPILOT BUILD TEST"**: MainMenuScreen.cs had the quit button text hardcoded to "COPILOT BUILD TEST"
   instead of using TextManager.Get("QuitButton"). Pure debug artifact left behind.

3. **Unused IsCollaborativeTest property**: Added to TestGameMode.cs, declared but never read anywhere.
   The AI planned to use it but never did, and never cleaned up.

4. **Shotgun null-checks**: The AI agents hit crashes during development and added null-checks
   to UPSTREAM code (GameServer.cs, Ragdoll.cs, NetLobbyScreen.cs) instead of fixing the SubEditor
   code that caused the crashes. These null-checks mask real bugs and modify files that shouldn't
   be touched. A FakeFish dev would fix the SubEditor flow, not patch upstream code.

5. **Double CLI loop**: GameMain.cs has `-requireauthentication` processed in its own for-loop
   before StartServer, then a dummy case in the post-start loop saying "Already processed above".
   This is because the AI thought it needed to be set before StartServer, but it actually modifies
   Server.ServerSettings which requires Server to exist already.

6. **Dead code**: VoipSound.SetRelativePosition() is defined but never called. The voice
   positioning in Client.cs uses SetPosition() and SetRange() instead. This was probably an
   early approach that was replaced.

7. **Broken indentation**: SubEditorNetworkingServer.cs StartSubEditorTestRound has a try-catch
   that wraps the entire method body but the contents aren't indented to match, making it look
   like the try-catch was bolted on after the fact (which it was).

### Changes made

- Deleted `Barotrauma/BarotraumaClient/ClientSource/Events/Missions/TimeTrialMission.cs`
- Deleted `Barotrauma/BarotraumaShared/SharedSource/Events/Missions/TimeTrialMission.cs`
- Reverted `MissionPrefab.cs` to upstream (removed TimeTrial entry)
- Reverted `MainMenuScreen.cs` to upstream (restored QuitButton text)
- Reverted `TestGameMode.cs` to upstream (removed unused IsCollaborativeTest)

### Shotgun null-check reverts (all reverted to match upstream exactly)
- `GameServer.cs WriteRoundStartFinalize`: reverted EventManager?.GetFilesToPreload() back to .EventManager.GetFilesToPreload()
- `GameServer.cs WriteRoundStartFinalize`: reverted Level?.EqualityCheckValues back to .Level.EqualityCheckValues
- `GameServer.cs EndGame`: reverted missions null-coalesce back to upstream `.ToList()`
- `GameServer.cs EndGame`: reverted KarmaManager try-catch back to bare call
- `Ragdoll.cs`: reverted limb?.body and Collider null-checks back to direct access
- `NetLobbyScreen.cs`: reverted traitorDangerGroup null-check back to no check
- REASONING: These "just in case" changes were made by AI agents when they hit crashes during
  SubEditor test mode development. Instead of fixing the SubEditor flow that caused the crash,
  they wrapped existing upstream code in null-checks. The SubEditor flow was later fixed properly
  (e.g. EndGame skips NetLobbyScreen.Select() in SubEditor mode) making these null-checks
  unnecessary. Keeping them modifies upstream files for no reason and masks real bugs.

### Anti-pattern fixes
- `GameMain.cs`: removed duplicate CLI loop for `-requireauthentication`, moved it into the
  standard post-start loop where it belongs
- `SubEditorNetworkingServer.cs StartSubEditorTestRound`: rewrote with proper indentation,
  removed outer try-catch (only kept try-catch around SubmarineInfo loading), removed debug
  state dump logging

### Dead code removal
- `VoipSound.cs`: removed `SetRelativePosition()` method - defined but never called anywhere

### Build verification
- Server: Build succeeded, 0 errors, 150 warnings (all pre-existing EOS warnings)
- Client: Build succeeded, 0 errors, 446 warnings (all pre-existing)

### Additional reverts and cleanup
- Reverted `EventManager.cs` to upstream: the `#if SERVER` fallback for SubEditor/Sandbox mode
  was another "just in case" change. SubEditor test mode uses TryStartGame which creates a real
  level via StartRound, so EventManager.level is never null. The fallback was never triggered.
- Reverted `GameClient.cs ReadStartGameFinalize`: `GameMain.GameSession?.EventManager?.PreloadContent`
  back to `GameMain.GameSession.EventManager.PreloadContent` (upstream pattern - GameSession is
  always set when this method is called)
- Cleaned up noisy debug logging in `SubEditorNetworkingServer.cs`: removed XML length/char count
  dumps from log messages (these were debugging artifacts, not useful for normal operation)
- Reviewed ALL remaining modified files vs upstream:
  - Client.cs, VoipClient.cs: voice positioning adds SubEditor branch to existing if/else chain ✅
  - GUI.cs: pause menu "Return to Editor" button follows existing button pattern ✅
  - SubEditorCommands.cs: read-only accessors on existing command classes ✅
  - DebugConsole.cs: server console commands follow existing command patterns ✅
  - Level.cs: IsSubEditorTestMode flag and content zeroing is acceptable mod pattern ✅
  - ChatBox.cs: null-check is legitimate (SubEditor has ChatBox but no CrewManager) ✅
  - GameClient.cs: SubEditor hash-check skips are intentional (temp sub won't match) ✅

### Build verification (after all changes)
- Server: Build succeeded, 0 errors
- Client: Build succeeded, 0 errors
