# Session prompt — Amendment A69 (paste this whole block into a fresh session)

You are running **Amendment A69 — Code Style Unification** on the DOTS Animation Toolkit package
in this repo. The spec is `Docs/AnimationToolkit/Amendment_A69_CodeStyleUnification_Spec.md`.
Read it in full before opening any other file, then follow its "Read first" list (§3) in order.
Every decision is already made in the spec's §2; do not re-ask the owner about naming or how
much to cut. If the code disagrees with a name the spec uses, grep for the current name, follow
the code, and note the drift in the spec's §6 log.

Do the tasks in §4 in order, T1 through T7. T1 writes three conformance tests that must be
**red** on today's code — commit them red, that is intended. T2 is the rename commit and is not
parallel-safe: package, game consumers, tests and docs change together, and the gate is green
only when both the package assemblies and the game assemblies compile. T3, T4, T5 and T6 are
marked `[parallel-safe]`: spawn one subagent per task (T5 may be split by folder into two), give
each the spec path and its task text, and let them edit files. **Subagents never call any
`mcp__UnityMCP__*` tool.** When they return, you run one compile gate over everything, run only
the fixtures each task names, and commit each task separately with the `A69-Tn:` prefix,
staging paths explicitly (never `git add -A`).

The rule you are applying (spec §2.3): one `<summary>` per file on the primary type, three
lines maximum; no `<remarks>`, no `<para>`/`<strong>`/`<em>`; no "architecture section",
"amendment A", "Phase", "§" or "rule Vnn" citations anywhere in non-test sources; a field
comment only for a sentinel, unit or ordering trap, one line; a `//` only for a why the code
cannot say, two lines at most. `Play` needs no comment. A blob field needs no comment. If a
comment is the only thing making a name understandable, rename the thing instead — except
blob fields, which are not renamed in this amendment. Do not change logic while stripping
comments; if a method makes you want to fix it, log it in §6 and move on.

Finish at T7 with `Conformance_F`, `Conformance_G`, `Conformance_H` green, the §1 measurement
script run before and after with both results pasted into §6, all four full suites run once
with discovered totals matching the baseline you recorded at T1, HANDOFF §2 carrying the new
rule and §4 carrying one paragraph on what landed. Then stop at the ⏸ owner checkpoint and tell
the owner which three files to open side by side with the previous commit.
