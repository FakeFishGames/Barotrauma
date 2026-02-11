# Build and Test Guide

## Quick Start

```bash
cd ~/projects/Barotrauma

# Get the latest code
git fetch origin copilot/fix-item-teleportation-bug
git checkout copilot/fix-item-teleportation-bug
git pull origin copilot/fix-item-teleportation-bug

# Clean and build (use ./build.sh clean for a one-liner)
rm -rf Barotrauma/bin/ReleaseLinux Barotrauma/BarotraumaClient/obj Barotrauma/BarotraumaServer/obj Barotrauma/BarotraumaShared/obj
dotnet build Barotrauma/BarotraumaServer/LinuxServer.csproj -c Release
dotnet build Barotrauma/BarotraumaClient/LinuxClient.csproj -c Release

# Run the client
cd Barotrauma/bin/ReleaseLinux/net8.0/
./Barotrauma
```

Or use the build script (does the same thing):
```bash
cd ~/projects/Barotrauma
git fetch origin copilot/fix-item-teleportation-bug
git checkout copilot/fix-item-teleportation-bug
git pull origin copilot/fix-item-teleportation-bug
./build.sh clean
cd Barotrauma/bin/ReleaseLinux/net8.0/
./Barotrauma
```

## Troubleshooting

### "I built it but I'm still running old code"

After switching branches, `dotnet build` sometimes reuses cached artifacts from the old branch.
**Always clean first** when switching branches:

```bash
rm -rf Barotrauma/bin/ReleaseLinux Barotrauma/BarotraumaClient/obj Barotrauma/BarotraumaServer/obj Barotrauma/BarotraumaShared/obj
```

Or use `./build.sh clean` which does this automatically.

### "I'm in detached HEAD state" / "I checked out an old commit and now I can't get back"

If you previously ran `git checkout <some-commit-hash>`, git put you in "detached HEAD" state.
To get back to the branch:

```bash
cd ~/projects/Barotrauma
git checkout copilot/fix-item-teleportation-bug
git pull origin copilot/fix-item-teleportation-bug
```

**Watch for typos in branch names!** The branch is called:
- ✅ `copilot/fix-item-teleportation-bug` (correct)
- ❌ `copilot/fix-item-teleportation-bugs` (wrong — extra `s`)

If you get "error: pathspec ... did not match any file(s)", the branch name is wrong.
Run `git branch -a` to see all available branches.

### "I have uncommitted changes blocking checkout"

```bash
git stash                # saves your changes temporarily
git checkout copilot/fix-item-teleportation-bug
git pull origin copilot/fix-item-teleportation-bug
git stash pop            # restores your changes (optional)
```

### "How do I test a specific old commit then come back?"

```bash
# Save where you are
git stash                                           # only if you have changes

# Test the old commit
git checkout 676b10608ab347121c7114deb44ee9ec688b3518
./build.sh clean
cd Barotrauma/bin/ReleaseLinux/net8.0/ && ./Barotrauma

# Come back to the branch (note: no trailing 's'!)
cd ~/projects/Barotrauma
git checkout copilot/fix-item-teleportation-bug
git stash pop                                       # only if you stashed
./build.sh clean
```

### "Nuclear option — start completely fresh"

If git is in a confusing state and nothing seems to work:

```bash
cd ~/projects
rm -rf Barotrauma
git clone https://github.com/girlyguppy/Barotrauma.git
cd Barotrauma
git checkout copilot/fix-item-teleportation-bug
./build.sh clean
cd Barotrauma/bin/ReleaseLinux/net8.0/
./Barotrauma
```

## What This Branch Is

`copilot/fix-item-teleportation-bug` is a fix for the item teleportation bug in the collaborative
submarine editor. It's based on `copilot/consolidate-copilot-branches-again` which is the
cleaned-up version of the collaborative editor mod, based on `master` (which tracks
FakeFishGames/Barotrauma upstream).

When you build this branch, you get: **the normal game + the collaborative editing feature**.
Everything else is identical to the upstream game.

## Testing the Mod

### Solo test (verify it doesn't break anything)
1. Build and launch the client
2. Open Submarine Editor from the main menu
3. You should see Host/Join buttons in the top bar
4. Load any submarine, edit it normally — all standard editor features should work
5. Play a normal multiplayer game — nothing should be different

### Multiplayer test (the actual feature)
1. Build both server and client (see above)
2. Launch client #1, open Submarine Editor, click "Host"
3. Launch client #2, open Submarine Editor, click "Join" with the host's IP:port
4. Both editors should see each other's cursors and live entity changes
5. Host can click "Test" to start a multiplayer test round
6. After testing, all clients return to the editor

### Server console commands (if running a dedicated server)
- `subeditor_status` — show connected editors and session state
- `subeditor_sethost [name]` — change who has host privileges
- `subeditor_starttest` — force-start a test round
- `subeditor_endtest` — end test round and return to editor

## Branch Cleanup

The old work-in-progress branches can be deleted. GitHub keeps deleted branches
recoverable for a period after deletion.

### How to delete old branches on GitHub

**Option A: GitHub web UI (easiest)**
1. Go to https://github.com/girlyguppy/Barotrauma/branches
2. Click the trash icon next to each old branch
3. GitHub shows a "Restore" button immediately after deletion if you change your mind

These branches can be deleted:
- `copilot/fix-collaborative-editing-bugs` (PR #5 — superseded by this branch)
- `copilot/fix-feature-implementation` (PR #4 — superseded)
- `copilot/fix-unsaved-submarine-loading` (no PR — superseded)
- `copilot/resume-multiplayer-subeditor` (PR #3 — superseded)
- `copilot/update-cloned-repo-and-features` (PR #2 — already merged, caused master pollution)
- `collaborative-submarine-editing` (old base branch — superseded)

**Option B: Command line**
```bash
# Delete remote branches (GitHub keeps them recoverable)
git push origin --delete copilot/fix-collaborative-editing-bugs
git push origin --delete copilot/fix-feature-implementation
git push origin --delete copilot/fix-unsaved-submarine-loading
git push origin --delete copilot/resume-multiplayer-subeditor
git push origin --delete copilot/update-cloned-repo-and-features
git push origin --delete collaborative-submarine-editing
```

**Option C: Close the PRs first, then delete**
1. Go to each PR (#2, #3, #4, #5) on GitHub
2. Click "Close pull request" at the bottom
3. GitHub will offer a "Delete branch" button after closing

### Recovering deleted branches
If you delete a branch and need it back:
- Go to the closed PR page — GitHub shows a "Restore branch" button
- Or use: `git reflog` locally if you had the branch checked out

### What to keep
- `master` — your clone of upstream FakeFishGames/Barotrauma
- `copilot/consolidate-copilot-branches` — the final mod branch (this one)
- `dev` — upstream dev branch (not ours)
- `LevelToStructureConverter` — separate feature branch (not related to us)
- `subeditor-background-picker` — separate feature branch (not related to us)

## PR to Upstream

When ready to submit to FakeFishGames/Barotrauma:

1. Close all old PRs (#2–#5) and delete their branches
2. Merge `copilot/consolidate-copilot-branches` into your `master` to clean up
   the pollution from PR #2 (TimeTrialMission, COPILOT BUILD TEST, etc.)
3. Fork FakeFishGames/Barotrauma on GitHub if you haven't already
4. Push the clean branch to your fork
5. Open a PR from your fork's branch → FakeFishGames/Barotrauma `master`

The diff will show only the mod changes (16 files, ~3800 lines added) with
no fabricated files, no debug artifacts, and no modifications to upstream
code that isn't needed for the feature.
