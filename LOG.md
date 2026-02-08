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
