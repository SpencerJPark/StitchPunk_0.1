# RC1 — the path from 0.61.0 to 1.0 (owner guide, 2026-09-30)

The roadmap is done: A82–A109 are built and merged, the package reads `0.61.0`. What is left is **release
readiness, not features** (`Assets/_Vault/Tasks/Claude/Code_Audit_2026-09.md`).

## What RC1 covers

1. **Demo scene** under `Samples~` — a buyer opens it and sees the toolkit work, nothing from `Assets/` in it.
2. **Benchmark sample** — the store page's performance number, and the scene that produces it.
3. **Bone-reparent guard** — a rig's hierarchy changes under a baked clip: detect, warn in Health, offer a rebake.
4. **Known defects** — each fixed, or listed in a shipped `KNOWN_ISSUES.md` with a reason. Nothing silently dropped.
5. **Customer-facing docs** — `Documentation~/` read as a buyer; there is no VAT Bake page yet.
6. **`package.json` store copy** — display name, description, keywords, URLs, Unity floor.
7. **Licence** — the file ships inside the package.

## Steps

1. **Decide the licence first.** Asset Store EULA only, or your own on top. It is the one call that blocks the rest.
2. **Spec session.** Fresh session, paste `Assets/_Vault/Spencer/next-session-rc1-spec-prompt.md`. It writes
   `RC1_ReleaseCandidate_Spec.md` in this folder plus the build prompt. It reconciles the defects table against
   `0.61.0` first, records settled calls as `RC1-D<n>`, and leaves a short "ask the owner first" list — answer it.
3. **Build session.** Paste the build prompt; it runs `/worktree-run` (a spec-lead per worktree, Sonnet workers).
   Your part: keep the Editor open, commit nothing to `main` while worktrees are open (including Editor `.meta`
   files), and dismiss any modal Unity dialog — one freezes both the gate broker and MCP.
4. **Checkpoint at the PC.** The real player build (never run unattended), a look at the demo and benchmark scenes,
   and your call on CT3 (the cutscene rail's opacity). Every other unseen checkpoint closes as accepted.
5. **Ship.** `1.0.0` + changelog, a final clean-project import check, the Asset Store listing from `package.json`.

## In parallel (game, not package)

The Player Resource + Summon Cost spec (`next-session-player-resource-prompt.md`) gives the game its loop; the
Scene 03 vertical slice follows it. Run it in a separate session alongside RC1.
