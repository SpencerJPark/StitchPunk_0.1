---
name: video-script
description: Write a devlog video script + shot list for Stitch Punk, sourced from real code, vault docs, and git history. Every shot must name one of four capturable sources (Play mode, Editor UI, Code, Art). Asks short-form vs long-form first.
---

# Devlog script

**Ask format first:** short-form clips (vertical, <60–90s — and how many) vs long-form (8–15 min YouTube)
vs both (long-form plus a shorts-cut list). Recommend a default: one small feature → short-form;
a week of work or a whole system → long-form. Confirm the time window if the request was vague.

**Ground it in reality** — read the git log for the window, the relevant `_Vault/Memories/Code/*.md`, and
`_Vault/Memories/Marketing/Strategy.md` for tone. Planned entries live in `Spencer/Content_Recording.md`.

## Rules that matter

- **The hook is the player-facing result** ("my zombies just learned to run for their lives"), never the
  tech ("today I implemented a utility AI scoring curve"). Open casual, go deep mid-video, close by
  connecting back to the game.
- **Two block kinds:** `[FACECAM]` = 3–6 talking-point bullets, not sentences to read aloud (used for the
  personal beats — why it was built, what went wrong). `[VO — <footage>]` = written narration to read over
  footage; full sentences, contractions, honest and direct, no marketing adjectives.
- **Every shot names a capturable source**, one of exactly four:
  1. **Play mode** — name the scene and the behaviour to trigger ("two citizens talking; wait for the 10s Talk loop").
  2. **Editor UI** — name the window and what should be visible (Entities Hierarchy, Shader Graph, Test Runner).
  3. **Code** — name the file and the region (`FleeAwarenessSystem.cs`, the priority-4 break-off check).
     One striking snippet beats scrolling.
  4. **Art / design docs** — name the asset path.
- **Never call for footage that doesn't exist yet.** If the honest footage is "the Editor and some code",
  write for that.
- **Loose, not locked.** Rough time ranges; mark optional depth `(cut if long)`. It is a scaffold for a
  solo dev recording in one take.

## Output

`Assets/_Vault/Videos/YYYY-MM-DD_<slug>.md` (a batch of shorts = one file, one section per clip).
Frontmatter: `tags: [video, devlog|short, draft]`, `related: "[[Memories/Marketing/Strategy]]"`, and the
source commit range or system name. If it fulfils a planned entry in `Content_Recording.md`, update that
entry's `Script:` line to point at the new file. Close by giving the recording order:
shot list first (raw, long takes), then VO against the script, facecam last.
