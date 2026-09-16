Continue the editor UI pass on the DOTS Animation Toolkit. The package is at `0.56.0`; trunk HEAD is `3184a427`
and the working tree is clean.

**Read first, in this order:**

1. `Assets/_Vault/Spencer/ui-pass-handoff-2026-09-15.md` — what shipped, what is left, the loop that works, and the
   facts that cost real time. Do not re-derive them.
2. `Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png` — my own capture of the rebuilt Ragdoll tab. **Look at
   the image.** It is the target composition.
3. `Docs/AnimationToolkit/EditorStyleGuide.md` — the approved guide, rules R01–R22, decisions SG-D1–SG-D8.
4. Root `CLAUDE.md` for the repo conventions, and `Assets/_Vault/Memories/Code/AnimationToolkit.md` for the A104
   traps and the capture recipe.

**The current state of all 15 tabs is captured in `Library/UIAudit/now/`.** Look at those before planning anything;
`Library/UIAudit/before/` is what they looked like before this work started.

**Work through the handoff's §3 list in order, top down:**

1. Two-tone the six flat tabs (Clip Editor, Retarget, Actor Profiles, Stats, Capture, Cutscenes).
2. Designed empty states in the empty viewports.
3. Clip Editor's and Actor Profiles' inspectors become cards with an aligned label column.
4. Stats becomes flat cards.
5. Rigs target rows: build SG-D6's **Target detail card** — three width passes already failed and were reverted;
   the row is 330px and cannot hold a name plus two chips.
6. Cutscenes magenta: **prove the source first** (scene materials versus a toolkit proxy), then fix.
7. Retarget's roster chips become real badges.

**How to work:**

- Parallel `worker` agents, one or two files each, files disjoint, exact line numbers and builder signatures pasted
  into every brief. Workers never call Unity MCP.
- Wait for the whole wave before compiling. Then: `refresh_unity` (compile, force) → `editor/state` →
  `read_console` → full `DotsAnimationToolkit.Tests.EditMode` (876 expected, ~10s).
- **Capture and look at every change.** Check `EditorApplication.isFocused` first; never save a stale frame under a
  capture name. Restore my active tab afterwards.
- When a capture is ambiguous, measure the hierarchy in `execute_code` (`worldBound`, padding, margins, resolved
  background) instead of guessing.
- Commit per finished piece, with a message that says what was wrong and what changed. Push when green.

**Rules:**

- **Remove no features and lose no information** — restyle only.
- No inline visual styles from C# (`Conformance_I`), no colour literals or off-scale font sizes in
  `ToolkitComponents.uss` (`Conformance_J`), and never raise a ratchet pin to make a change fit.
- Do not restyle the tab list or the two-tone recipe — both are settled and I signed off on them.
- Play mode is not authorized.

Do not check in with me constantly. Work down the list, capture as you go, and show me the results when you have
something worth looking at.
