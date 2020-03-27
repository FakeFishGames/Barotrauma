---
name: Release checklist
about: A checklist that should be gone through when releasing a hotfix/patch/update
title: v0.0.0.0
labels: Code, CM
assignees: ''

---

**Code:**
- [ ] Build and upload dedicated server
- [ ] Verify that Vanilla content package hashes match between Windows/Mac/Linux
- [ ] Run "checkmissingloca" command to make sure localization files are up-to-date.
- [ ] Install Trick or Trauma and check you can start a round with no obvious issues/errors (to make sure we didn't unintentionally break compatibility with older mods).
- [ ] Prepare new main menu content (changelog)
- [ ] Prepare public github repo for pushing the new changes

**CM:**
- [ ] Prepare Steam announcement
- [ ] Prepare Discord announcement (if needed)
- [ ] Prepare blog post (if needed)

**Code (when everything above is ready):**
- [ ] Publish
- [ ] Upload new main menu content
- [ ] Update public github repo
- [ ] Merge release to master and active branches

**CM (after the update is live):**
- [ ] Post Steam announcement
- [ ] Post Discord announcement (if needed)
- [ ] Blog post (if needed)
- [ ] Clean up Trello
