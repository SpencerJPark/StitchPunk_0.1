---
title: Test Dialogue System End-to-End
status: active
created: 2026-04-13
area: code
---

## Goal

Validate the full dialogue pipeline after the Refresher node runtime was wired up. The runtime now picks Start vs Refresher based on whether the sequence has been played — this needs a real scene test to confirm everything works together.

## Steps

### Scene setup
- [ ] Create a test `DialogueSequenceSO` asset in `Assets/_Scripts/Data/SOs/`
  - Set a unique `sequenceId` (add a matching constant to `DialogueIds.Sequences` if one doesn't exist)
  - Build a **primary path**: Start → Line ("Hello, first time!") → End
  - Build a **refresher path**: Refresher → Line ("We've spoken before.") → End
- [ ] Place an NPC GameObject in the test scene with `DialogueProviderAuthoring` pointing to the new SO
- [ ] Make sure the NPC also has `InteractionAuthoring` (with `playerInteractable = true`) or confirm `DialogueProviderAuthoring` adds `PlayerInteractable` on its own
- [ ] Add the SO to `DialogueUIManager.sequenceAssets` list in the Inspector
- [ ] Confirm `DialogueManagerAuthoring` singleton and `GameDataAuthoring` singleton are in the scene

### First-visit test
- [ ] Play the scene, walk up to the NPC and press Interact
- [ ] Confirm the primary path plays ("Hello, first time!")
- [ ] Confirm the panel closes when the End node is reached
- [ ] Confirm the sequence is now in `PlayedDialogue` (check via a breakpoint or add a debug log in `EndSequence`)

### Repeat-visit test
- [ ] Without stopping Play mode, interact with the NPC again
- [ ] Confirm the refresher path plays ("We've spoken before.") — NOT the primary path
- [ ] Confirm the panel closes again cleanly

### Edge cases
- [ ] Test a sequence with **no Refresher node** — repeat visit should still play Start path
- [ ] Test **Decision node**: add a branch in the primary path and confirm both branches reach End correctly
- [ ] Test **Event node**: set `eventId` to a known value and confirm `OnDialogueEvent` fires (add a temporary debug log in `DialogueEventSystem`)

### Compile checks
- [ ] No errors about missing `refresherSequenceId` field (removed from `DialogueProvider`)
- [ ] No errors about missing `refresherSequence` field on `DialogueSequenceSO` (removed — any existing SOs that serialized this will silently drop it)

## Notes

Key files to check if something breaks:
- `DialogueUIManager.cs` — `BeginSequence()` picks entry node, `HasBeenPlayed()` checks buffer
- `DialogueStartSystem.cs` — should now be ~40 lines, no refresher logic
- `DialogueProviderAuthoring.cs` — only bakes `sequenceId`, no refresher field

The `Refresher` node in the Dialogue Editor is teal and is blocked to one-per-tree. If you try to add a second one the editor logs a warning and ignores it.
