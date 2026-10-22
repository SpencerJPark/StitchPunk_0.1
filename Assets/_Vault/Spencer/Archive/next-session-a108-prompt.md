Build A108 on the DOTS Animation Toolkit — the chrome consistency pass I asked for after looking at the A105 work.
The package reads `0.56.0` and trunk HEAD is `29e9ba15`; the only uncommitted files are the A108 spec and the
roadmap line that points at it.

**Read first, in this order:**

1. `Assets/_Vault/Tasks/AnimationPackage/A108_ChromeConsistencyPass2_Spec.md` — the whole thing. §1 is decided, §2
   is already diagnosed down to file and line, §5 is the wave plan with the briefs. **Do not re-derive §2** —
   every line number in it was read off the files on 2026-09-17.
2. `Docs/AnimationToolkit/EditorStyleGuide.md` §2–§4 — the approved tokens, components and rules R01–R22.
3. Root `CLAUDE.md`, and `Assets/_Vault/Memories/Code/AnimationToolkit.md` for the capture recipe and the A104
   traps.

**What I asked for, in my words:** space between the cards and the column borders in Actor Profiles' layers and the
Actor Inspector; buttons that look like one family instead of a big white rounded Bake beside a tiny square Save;
icons on a white button that don't blend into it; and the collapsed tab bar icons redrawn — one tone, recentred,
and actually different from each other. Rigs should show bones. Ragdoll should show a body.

**Order of work:**

1. **T0 first.** Read `package.json` and take the next unused minor (the spec says `0.57.0` unless A105's close
   already claimed it) — record it in the spec's §7. Then run the tint probe in §4.1 **yourself**, in a floating
   utility window, and write the verdict into §7 before you brief anybody. Everything in wave A quotes it.
2. **Before captures** per §4.2 into `Library/UIAudit/before-a108/`, including the tab strip narrowed until it is
   in icon mode.
3. **Wave A** (7 workers, parallel) — the shared style layer, the gutters, the variant sweep. **Wave B** (6
   workers, parallel) — the fifteen drawn glyphs and the tab strip wiring. **Wave C** — the ratchet fixture and
   the close.
4. After captures, the full suites once, merge, then the close list in §6.

**How to work:**

- Parallel `worker` agents. One or two files each, files disjoint inside a wave, exact line numbers and the
  snippets they need pasted into each brief, "at turn 30 stop editing and write your report", reports ≤30 lines.
  A capped agent is never resumed — read its diff and spawn a fresh worker with what's left.
- **Workers never call Unity MCP.** You own the probe, every compile gate, every capture and the merge.
- Gate **once per wave**, not per task: `refresh_unity` → `editor_state.isCompiling` → `read_console`. Only wave
  C runs fixtures (the touched three); the full ~700-test suite runs once at the close.
- **Capture and look at every change.** Check `EditorApplication.isFocused` first; an unfocused grab returns a
  stale frame. Scale by `pixelsPerPoint`. Restore my active tab afterwards.
- When a capture is ambiguous, measure the hierarchy in `execute_code` (`worldBound`, resolved padding, margins)
  instead of guessing.
- Commit per wave, `A108-Wn:` / `A108-Xn:` prefixes, staging paths explicitly, never `git add -A`. Push when green.

**Rules:**

- **Restyle only — remove no features and lose no information.**
- No inline visual styles from C# (`Conformance_I`), no colour literals or off-scale font sizes in the shared
  sheets (`Conformance_J`), and never raise a ratchet pin to make a change fit.
- Keep the tab list's look and the two-tone column recipe — both are settled.
- The cutscene panel is out of scope; A107 owns it. Its buttons inherit the new Secondary default and that is all.
- Every fixture you keep must fail when you revert its fix. If it passes both ways, delete it.
- Play mode is not authorized. Leave the Editor open unless I say otherwise.

The three questions in §7's checkpoint are for me to look at when you're done — write them up with the captures,
don't stop and wait on them.
