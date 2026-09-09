# Session prompt — Amendment A78 (paste this whole block into a fresh session)

You are running **Amendment A78 — the rig says what to bake, and the bake does every VAT part** on
the DOTS Animation Toolkit package in this repo. The spec is
`Docs/AnimationToolkit/Amendment_A78_VatBakeSourceFromRig_Spec.md`. Read it in full — **especially
its §2, "What is already true"**, which is the difference between a two-day amendment and a two-week
one — then its §4 "Read first" list in order, then
`Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4, which is binding.

The spec's §3 decisions (A78-D1…D17) are settled. Do not re-ask the owner whether the field should be
disabled rather than deleted (deleted, replaced by a read-only line), whether resolution should test
`TargetKind.VatMesh` (it must not — nothing authors `kind`, so carrying a skinned mesh is the
signal), whether the bake may pose the prefab asset (never — one throwaway `Object.Instantiate` for
the whole run), whether `VatTextureBaker` changes (it does not — the panel calls it once per part),
whether `clipRanges` moves into the per-part entries (it stays flat and set-level, which is what
keeps the runtime untouched), or what happens to a part nothing animates (skipped with a named
warning; a bake with no parts left creates nothing), or whether the Rigs tab should author
`TargetKind` (it must — T9; without it a baked part gets no `VatDriven` at entity bake and renders as
a motionless clump, which makes the rest of this amendment unusable on an actor).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you compile,
run tests, drive the Editor and commit. The tasks are sized for `worker` subagents that edit files
only (spawn `worker`, never `general-purpose`; `verifier` for read-only checks) — each gets the spec
path, its task's text, its "Read" line, the §5 block it builds, the spec's §2, and the hard rules (no
`var`, no single-letter names, explicit types). Each must stay under ~100k tokens: at most two files,
named line ranges, no browsing, "at turn 30 stop editing and write your ≤30-line report", "never call
any `mcp__UnityMCP__*` tool".

Run the spec's waves:

- **T0 yourself:** gate, baseline totals, and the one platform probe. The probe's answer can change
  T3 (see §6 T0) — run it before you spawn anything.
- **Wave 1, four subagents at once:** T1, T2, T6, T9 (all `[parallel-safe]`, disjoint files). Wait
  for all four, then **one** compile gate and their eight fixtures by `test_names`. T6 has no
  fixture — it compiles or it does not.
- **Wave 2:** T3 (one subagent). Gate. Nothing to run — no fixture, by design.
- **Wave 3, two subagents at once:** T4, T5. Gate, plus the existing baker fixtures.
- **T7 yourself:** full gate, the three drives, docs, changelog, version, vault note, HANDOFF §4.
- **Stop at T8**, the ⏸ owner checkpoint, with the message the spec gives. Do not continue past it.

Commit each task with an `A78-Tn:` prefix, staging paths explicitly, never `git add -A`. Push when
green.

## State you are building on

- **0.25.0 (A77)** is head. A78 takes `0.26.0` unless `CHANGELOG.md` has moved.
- **A76 (0.23.0)** is why this is possible: every rig now carries a Source Prefab and a tickable
  target list, so the bake tab's third field asks for something the rig already knows.
- **A74 (0.20.0)** owns `VatPreviewElement` and the preview viewports. **A78 does not touch it.** The
  preview keeps showing one part; the toggles the owner asked for are A79's job. If you find yourself
  editing that file, stop — you are building the wrong amendment.
- The single-part subject is `Assets/ScriptableObjects/Animations/VatSampleTentacle/` — prefab, clips,
  rig, mesh, material, already on disk, `targets: []` and one skinned mesh named `TentacleMesh`. It
  resolves untargeted and its output filenames must not change. The two-part subject is what T6
  builds.
- Suite baselines: measure at T0. A78 adds eight EditMode tests and no PlayMode tests.

## The traps that will cost you a session if you rediscover them

- **The bake writes local TRS onto every bone of whatever renderer it is handed**
  (`VatTextureBaker.cs:190-249`, and it restores them in a `finally` precisely because it does).
  Handing it a prefab asset's renderer poses the asset on disk. T7's drive asserts `git status` shows
  `VatSampleTentacle.prefab` unmodified — that assertion is the point of the task, not a formality.
- **`DestroyImmediate` the bake instance only after the last `Bake` returns.** Each call's own
  `finally` stops `AnimationMode`; tearing the hierarchy out from under a live sampling session
  leaves the Editor in AnimationMode with no way back except a domain reload.
- **Pass the socket list on the first `Bake` call only** (A78-D5). `VatTextureBaker` samples sockets
  inside `Bake`, so N calls with the list would write N copies of every socket track, and nothing
  downstream would complain — the sword just rides the wrong pose.
- **`Conformance_G` allows exactly eight static-class suffixes** — Api, Builder, Sampler, Resolver,
  Math, Validation, Utility (and `Utility` only inside `Editor/ClipUtilities/`), Editing — plus a
  plain-noun allowlist. `VatBakeSourceResolver` is legal as named. A `…Helper`, `…Picker` or `…Utils`
  fails the gate; do not rename it and do not add an allowlist entry. `VatBakeSource` and
  `VatPartTextures` are instance classes, unaffected.
- **`Conformance_F`**: one `<summary>` per file, on the primary type, three lines at most, no
  `<remarks>`, no `§`, no amendment or phase citations in shipped sources. Copying this spec's prose
  into a comment is the most likely way to fail it.
- **`Conformance_E`** fails on any `Handles.`, `OnGUI` or `GUILayout` under `Editor/`. Labels,
  `ObjectField` and `EditorGUIUtility.PingObject` are all fine — T5's inspector is the one to watch.
  **T9 must use `GenericDropdownMenu`, not `GenericMenu`** — the latter is IMGUI and fails this gate.
  The working pattern is `ActorEditorInspectorColumn.cs:449-469`.
- **`Conformance_D` scans raw test-file text for host asset folder paths.** T1's and T2's fixtures
  build everything in memory and need no `Assets/`-prefixed literal. T6 must take its folder as a
  parameter, the way `CreateSampleAssets` already does — a hardcoded host path there cost A74 a gate
  cycle.
- **Never rebuild a pane from inside a value-changed callback.** The Rig callback sets the label's
  text and colour in place and refreshes the preview; it does not rebuild the Source column.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`.
  `resolvedStyle` / `layout` are stale in the call that built the UI; read them in a second call.
- **Captures scale by `pixelsPerPoint`** or you get the bottom-left corner, and `GrabPixels` returns a
  stale frame when `EditorApplication.isFocused` is false — check it first. If a capture fails, say so
  and record the label's `text` value instead of claiming a screenshot you do not have.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names and line
  numbers were verified on 2026-09-08 against a tree at `fd67f685`; if one drifted, grep, follow the
  code, and note the drift in the spec's §8.

## When you finish

Update the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed,
what is owed — the owner's checkpoint), the vault note section named in T7.7, and stop with the T8
message. Do not start A79.
