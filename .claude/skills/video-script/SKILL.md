---
name: video-script
description: Generate a loose devlog video script + shot list for Stitch Punk content — scripts sourced from the actual code, the vault design docs, and git history. Use whenever the user wants video content about their work — "make me a video script", "devlog for the X system", "weekly update of what I worked on", "script for a short/reel/tiktok", "YouTube video about the AI system", "turn this feature into content", or any request to script, outline, or storyboard a video about the game. Always asks for format (short-form clips vs long-form video) before writing. Do NOT use for: editing the marketing strategy docs, recording/editing software advice, or writing store-page / announcement copy.
---

# video-script

Turns what Spencer actually built — code, commits, vault design docs — into a recordable video script with a shot list. The output is **loose by design**: voiceover lines are written out so they can be read or riffed on, facecam segments are talking-point beats, and every shot is something Spencer can actually capture. The goal is that he can sit down with the script, record the footage from the shot list, record the audio, and have a video.

## Ground truth to read first

Before writing anything, read these two vault docs — they define the voice and the plan this content slots into:

- `Assets/_Vault/Memories/Marketing/Strategy.md` — audience (Unity/DOTS devs + indie game fans), platform priority (YouTube primary), and the **tone of voice** section. The tone rules are non-negotiable: honest and direct, not hype-driven; technical but accessible (explain ECS as "components of data instead of objects with methods", not jargon); enthusiastic about the craft; self-aware about solo-dev scope.
- `Assets/_Vault/Spencer/Content_Recording.md` — the planned devlog series (#1–#4 with milestone gates), the shorts backlog, and recording workflow notes. If the requested video matches a planned devlog, build on that entry's content outline and length target, and note in the output that it fulfils that slot.

## Workflow

### 1. Scope — what is this video about?

Two modes, inferred from the request:

**Feature mode** ("make a video about the flee system"): the subject is a system or feature. Find its real substance:
- The vault design doc: `Assets/_Vault/Memories/Code/*.md` for that area, plus any plan in `Assets/_Vault/Tasks/Plans/` or completed spec in `Assets/_Vault/Tasks/Verification/` / `Done/`.
- The actual source files — read enough of the code to pull **concrete specifics** (real component names, real thresholds like "flees when health < 30%", real system counts). Specific numbers and names are what make a devlog feel authoritative instead of generic; vague scripts come from vague research.

**Time-window mode** ("weekly update of what I worked on this week"): the subject is a period of work. Reconstruct it from git:
```powershell
git log --since="7 days ago" --pretty=format:"%h %ad %s" --date=short
git log --since="7 days ago" --stat --pretty=format:"%h %s"
```
Adjust `--since` to the requested window. Group commits into 2–4 **stories** (a story = a feature the audience can see or understand, not a commit). Fold noise — meta files, typo fixes, refactors — into the story they served, or drop them. Then do feature-mode research on each story so the script has substance, not just a commit-list readout.

If the scope is genuinely ambiguous (e.g. "make me a video" with no subject), ask — but prefer inferring from recent git history over interrogating.

### 2. Ask for format — always

Every run asks this before writing (one `AskUserQuestion` round; combine with any scope question):

- **Format**: short-form clips (Reels / TikTok / Shorts — vertical, under 60–90s each) vs long-form (YouTube devlog, 8–15 min per the Content_Recording targets) vs both (long-form script + a list of which segments cut into shorts).
- If short-form: **how many clips** — one clip, or a batch covering the material.
- If the subject was a time window, this is also where to confirm the window if the request was vague ("this week" on a Saturday vs a Monday differ).

Recommend a default based on the material: a single small feature → short-form; a week of work or a whole system → long-form with a shorts-cut list.

### 3. Write the script

Templates and a worked example live in `references/script-templates.md` — read it before writing. The rules that matter most:

**Structure (mixed-audience arc):** open casual, go deep. The hook is always the player-facing result ("my zombies just learned to run for their lives") — never the tech ("today I implemented a utility AI scoring curve"). Technical depth comes mid-video, after the viewer has seen why they should care. Close by connecting back to the game.

**Delivery tags** — every block is one of two kinds:
- `[FACECAM]` — talking-point beats, 3–6 bullets, not sentences to read. Spencer talks to camera; these anchor the personal/honest segments (why he built it, what went wrong).
- `[VO — <footage>]` — written narration meant to be read over footage, in Spencer's honest/direct voice. Full sentences, conversational, contractions, no marketing adjectives.

**Shot list — only footage that can actually be captured.** Every `[VO]` block and every B-roll callout names its source from exactly these four:
1. **Play mode gameplay** — units acting in `DOTSTestScene` or `Game.unity`. Name the scene and the behavior to trigger ("two citizens talking; wait for the 10s Talk loop").
2. **Editor UI** — Scene view, Inspector, Entities Hierarchy, Shader Graph, Test Runner. Name the window and what should be visible.
3. **Code / IDE capture** — name the file and the region worth showing (`FleeAwarenessSystem.cs`, the priority-4 break-off check). Prefer one striking snippet over scrolling.
4. **Concept art / design docs** — stills from the vault, sprites, character sheets. Name the asset path.

Never call for footage that doesn't exist yet (unbuilt scenes, features that don't run). If the honest footage is "the Editor and some code", write the script for that — the Strategy doc's audience watches for exactly that content.

**Loose, not locked.** Timings are rough ranges. Mark optional depth with `(cut if long)`. The script is a scaffold for a solo dev recording in one take, not a broadcast rundown.

### 4. Save and hand off

- Save to `Assets/_Vault/Videos/YYYY-MM-DD_<slug>.md` (create the folder on first use; Unity will generate `.meta` files — that's normal). A batch of shorts is one file with one section per clip.
- Frontmatter: `tags: [video, devlog|short, draft]`, `related: "[[Memories/Marketing/Strategy]]"`, plus the source commit range or system name.
- If the video fulfils a planned entry in `Content_Recording.md`, update that entry's "Script:" line to point at the new file.
- Close by telling Spencer the recording order: capture the shot list first (raw, long takes), then record VO against the script, facecam last.

## Reference files
- `references/script-templates.md` — long-form and short-form skeletons, shot-line format, worked example segment.
