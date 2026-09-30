# 0006: The package is `com.playtestcopilot`, embedded in Stitch_Punk

Status: accepted (2026-09-29)

## Decision
The Unity package is `Packages/playtest-copilot/` inside the Stitch_Punk repository, named
`com.playtestcopilot`, displayName "Playtest Copilot", MIT licensed. The starter kit stays at
`Assets/_Vault/playtest-copilot/` as the product spec and decision log.

The starter kit's `com.playtestcopilot.editor` name is retired: the package ships a Runtime
assembly too (the `[PlaytestTrack]` attribute and every recorder that must run inside play mode),
so an `.editor` suffix would be wrong.

## Why
Stitch_Punk already carries three embedded packages built this way, and each extracted to its own
repository cleanly when it was ready. Embedding means the Editor compiles the package the moment
it opens, and the game itself is the sample project the spec asks for: a real hybrid of
GameObjects and Entities rather than a toy.

## Consequences
`.gitignore`'s `/Packages/*/` rule needed a matching `!/Packages/playtest-copilot/` un-ignore. The
file already warns that this was missed once before, during a package rename, and silently dropped
every new file from every commit.
