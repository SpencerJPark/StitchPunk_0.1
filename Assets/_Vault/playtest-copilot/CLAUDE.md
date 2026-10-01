# CLAUDE.md: Playtest Copilot

Instructions for coding agents working in this repo.

## Source of truth

- `docs/SPEC.md` is the product spec. Read the **Build handoff** section first; it lists locked decisions, the repo layout and the milestones.
- If the code and the spec disagree, stop and ask. Do not quietly change the design.
- Decisions already made are recorded in `docs/decisions/`. Add a new record for any non-obvious choice you make.

## Current goal

Milestone 1: a fully local, end-to-end loop.

AI Play -> talk and circle things -> session folder -> transcript -> resolved references -> drafted spec -> review in Editor -> one Claude Code task in a local git worktree -> compile + tests -> diff.

Done when a 10-minute session on `sample-project/` produces at least three correct specs and one merges after passing tests, with no network use except the LLM API.

Do not start Milestone 2 (cloud) until Milestone 1 is done.

## Hard rules

1. **Local first.** Local mode must never require a cloud account or network access beyond the LLM API. Cloud features are opt-in and additive.
2. **Normal Play is untouched.** Only the AI Play button enables capture. No recording code runs in a plain Play session.
3. **Keep the Editor responsive.** No blocking work on the Editor main thread. Transcription, spec drafting and git work go to the Python companion service.
4. **Session folders are the contract.** Write exactly the layout in the spec (Editor experience > Session folder). Everything a user or agent needs must be readable there without the tool.
5. **Record key never steals game input.** Rebindable, warn on clashes with the project's Input System bindings, and offer the on-screen record button.
6. **Tests with every feature.** EditMode and PlayMode tests in `unity-package/Tests/`, pytest in `companion/`. Run them (Unity in batch mode) before reporting a task done.
7. **Small tasks.** Keep each task under ~100k tokens of context. Split by file ownership: one task owns each `.unity`, `.prefab`, `.asset` or `.cs` file it changes.

## Stack

- Unity 6.4+ (Entities available as core packages). C#, UPM package, UI Toolkit.
- Python 3.11+ for the companion service. whisper.cpp for local transcription. ffmpeg for clips.
- Claude Code as the first coding-agent adapter, behind an interface so others can be added.

## Icons

- `unity-package/Editor/Icons/PlayAI.png` (light skin) and `d_PlayAI.png` (dark/pro skin), with `@2x` versions at 32 px.
- Import them as Texture Type **Editor GUI and Legacy GUI**, no compression, no mipmaps.
- Pick the variant with `EditorGUIUtility.isProSkin`, and the `@2x` file on high-DPI displays.
- Redrawn 2026-10-01 at the owner's request, and no longer from `design/icons/`: the originals read
  as a foreign orange glyph next to Unity's own controls. The shipped icons are now **Unity's own
  `PlayButton` triangle**, read back from `EditorGUIUtility.IconContent` and composited with a plus
  in the free bottom-right corner — light grey (195) for the dark skin, dark grey (56) for the light
  skin. The SVGs in `design/icons/` are the superseded originals.
- To regenerate, blit the built-in icon to a RenderTexture and `ReadPixels` it: built-in icon
  textures are not readable, so `GetPixels32` on them throws.

## Before you ask the user

These are still open. Ask rather than guess:

- LLM provider and model for spec drafting, and where the API key lives
- Package name, license and repo visibility
- Which sample project to test against
