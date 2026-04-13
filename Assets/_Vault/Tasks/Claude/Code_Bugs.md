---
tags: [task, claude, code, bugs]
related: "[[Tasks/Claude/Code_Systems]], [[Memories/Code/Systems_AI]], [[Memories/Code/Gotchas]]"
---

# Code — Bug Fixes

Known bugs Claude can fix in a single session. Each has root cause + exact fix. Pick one up, read the linked memory file, execute.

---

## Priority 2 — UI / Verification

- [ ] **Update Rive package to latest version**
  - **Blocking:** `UnitSelectionBoxUI` verification
  - **Fix:** Update Rive package via Unity Package Manager
  - **Note:** Legacy `RiveAnimator.cs` in `Core/` does not need to be restored — just update the package

- [ ] **Wire minion selection indicator UI**
  - **Depends on:** Rive package update above
  - **What's needed:** `SelectedVisualSystem` (in `PresentationSystemGroup`) should show a visible indicator under selected minion bodies
  - **Component:** `Selected` (enableable) on body entities — `onSelected` / `onDeselected` bool flags are the trigger
  - **See:** [[Memories/Code/Components]] (Selected component), [[Memories/Code/Systems]] (PresentationSystemGroup)
