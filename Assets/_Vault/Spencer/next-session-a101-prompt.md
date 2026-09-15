You are running **Amendment A101 — Editor chrome consistency** on the DOTS Animation Toolkit package
(`Packages/com.dotsanimationtoolkit`). Spec: `Assets/_Vault/Tasks/AnimationPackage/A101_EditorChromeConsistency_Spec.md`.
Its §0 is the full session prompt; read the spec in full first, then the roadmap §3 protocol, then only what §3 names.

**Run this alone**, on trunk, after the A96F/A97F/A99 batch has merged (it edits `Editor/Materials`, `Editor/Retarget` and
`ClipEditorWindow.cs`, which those specs touch). Not through `/worktree-run`: one orchestrator, no spec-leads. If the batch
has not merged yet, stop and say so.

**Shape:** T0 (yours: baseline, allowlist scan, icon probes, before-captures) → wave 1 = seven `worker` subagents in parallel
(T1–T7, foundations whose surfaces §4 pins) → one gate → wave 2 = twenty-one `worker` subagents in parallel (T8–T28; T25 then T26
are sequential on the cutscene panel) → one gate → T29 rename sweep (yours, sed) → T30 docs worker → full suites → T31 drive with
after-captures → T32 close → stop at T33 with the checkpoint message the spec gives.

**Worker contract (paste into every brief):** the spec path; the task text; §4.10 verbatim for a wave-2 task; the §4 block it
builds against; at most the two files the task names, at the line ranges it names; hard rules (no `var`, no single-letter
names, explicit types; one `<summary>` per file ≤ 3 lines; no `§`, amendment numbers or spec citations in shipped code); "at
turn 30 stop editing and write your report; report ≤ 30 lines; never call any `mcp__UnityMCP__*` tool." Spawn from the repo root.
A capped worker is never resumed: read its diff, spawn a fresh worker for what is missing.

**Owner pre-answers (2026-09-15):** the Clip Editor and Texture Packer are the reference and must look unchanged; spacing,
colours and reuse are the point; the cutscene timeline shows its rows like the Clip Editor when nothing is selected (spec D6,
with D7 as the alternative reading to confirm at T33). Models: workers `sonnet`.

**Cautions:** captures need the Editor focused (`reference_editor_capture_dpi_scale`); the Burst JIT cache can throw hash
errors on untouched files after a large wave — check `Logs/Editor.log` line order before blaming a worker; `Conformance_I` is
expected red after wave 1 and green after wave 2 — that is its revert-to-fail proof, record both counts in §7.
