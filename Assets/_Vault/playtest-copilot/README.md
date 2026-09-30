# Playtest Copilot for Unity

An AI playtest mode for the Unity Editor. Press **AI Play** instead of Play, talk through what you want changed, pause and circle things on screen, and the tool turns your session into reviewed specs that coding agents like Claude Code can implement in small, parallel tasks.

This folder is the starter kit: the spec, agent instructions, decision records and the AI Play icons. It has no code yet.

## What's in here

| Path | What it is |
| --- | --- |
| `docs/SPEC.md` | Full product spec. The **Build handoff** section is the brief for a coding agent. |
| `CLAUDE.md` | Rules and current goal for coding agents working in the repo |
| `docs/decisions/` | Short records of the decisions already made |
| `unity-package/Editor/Icons/` | AI Play toolbar icons: light and dark skin, 16 px and 32 px |
| `unity-package/package.json` | UPM package manifest to build the Editor package on |
| `design/icons/` | Icon sources (SVG) and a preview image |
| `.gitignore` | Ignores Unity build folders and local playtest sessions |

## How to use it

1. Unzip into an empty folder and `git init` it (or copy into your repo root).
2. Open the folder with Claude Code (or another coding agent with repo access).
3. Say: "Read CLAUDE.md and docs/SPEC.md, then plan Milestone 1 as small tasks and start with the first one."
4. Answer the three open questions in `CLAUDE.md` when the agent asks: LLM provider, package name and license, sample project.

## Icon import settings in Unity

Select the PNGs in `unity-package/Editor/Icons/` and set:

- Texture Type: **Editor GUI and Legacy GUI**
- Compression: **None**
- Generate Mip Maps: **off**

`PlayAI.png` is for the light Editor skin and `d_PlayAI.png` for the dark skin. The `@2x` files are for high-DPI displays.

## Status

Spec complete, build not started. Milestone 1 is a fully local loop; Milestone 2 adds optional cloud team mode on GCP.
