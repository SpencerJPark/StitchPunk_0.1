You are writing **one spec** that merges Unified Clip Authoring (UA) P1–P5 with A79 (VAT Bake preview shows the other
parts), per `Assets/_Vault/Tasks/Claude/Code_Audit_2026-09.md` §3 item 2 and §5 item 3. You write the spec and its
worktree build plan; you do not build it. The package is at `0.54.0` after A102 (release readiness, 2026-09-15): the
EditMode suite is fully green (868, zero failures), PlayMode 285.

Read, in order:
1. Root `CLAUDE.md`; `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §2 (standing owner calls) and §3
   (execution protocol).
2. `Code_Audit_2026-09.md` §3 item 2 — the merge rationale and the three decisions to **record, not re-ask**:
   - UA P1 (b): a targetless rig is fine when the clip set has bone or VAT tracks;
   - UA P2: build an empty-target blob so one code path serves every kind;
   - UA P4: read-only lanes only, no import.
3. `Assets/_Vault/Tasks/NewPlans/UnifiedClipAuthoring_System.md` whole (P0 built 2026-09-08; P1–P5 specced with three
   ← DECISION markers — the three above settle them).
4. `Docs/AnimationToolkit/Amendment_A79_VatPreviewModes_Spec.md` whole. The vault note `project_vat_bake_specs` says A79
   needs its T0 grounding pass first: per-part VAT already works at run time, only the baker is single-mesh.
5. `Docs/AnimationToolkit/HANDOFF.md` §5–§7 and `Assets/_Vault/Memories/Code/AnimationToolkit.md` (grep `poser`, `VAT`,
   `A79`, `Actor Editor`), then ground every type name the two specs use by grep against the current tree — both were
   written weeks and ~25 minor versions ago.

Write `Assets/_Vault/Tasks/AnimationPackage/A103_UnifiedAuthoringAndVatPreview_Spec.md` in the A102 shape (status line,
§0 session prompt pointer, §1 decisions with ids, §2 files, §3 read ranges, §4 fixtures with revert-to-fail, §5 lead
tasks with `[parallel-safe]` markers and ≤2 files per worker, §6 stage tasks for anything Editor-bound, §7 empty log).
Order: UA P2's per-kind poser split first; then UA P3 (VAT in the Clip Editor) and A79 (cutouts in the VAT Bake preview)
both build on it; P1, P4, P5 where they fall. Take the next free minor (`0.55.0`). Add it to the roadmap as a new box,
mark both source specs "superseded by A103" in their status lines, and write the stage-orchestrator prompt for a
`/worktree-run` of it beside this file (`next-session-a103-prompt.md`), copying the lead contract from
`next-session-parallel-a102-despawn-minionorders-prompt.md`'s predecessor chain.

Owner calls already settled (do not re-ask): names never numbers; no manual asset wiring; no package-side event handlers;
no sound mixing; sprite sheets are Texture2DArrays; new rigs are created fresh, no migration paths; unseen checkpoints
close as accepted unless game breaking; visual-first editor tools (the owner rejects authoring surfaces without live
visual feedback). Anything genuinely new that only the owner can decide becomes a ⏸ checkpoint question in the spec,
not a guess.

Commit the spec, the roadmap box, the two status lines and the prompt with an `A103-spec:` prefix; push.
