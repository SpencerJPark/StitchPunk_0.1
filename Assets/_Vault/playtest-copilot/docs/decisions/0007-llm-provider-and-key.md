# 0007: Claude for spec drafting, key from the environment

Status: accepted (2026-09-29)

## Decision
Spec drafting calls the Claude API: `claude-opus-5` to write a spec, `claude-sonnet-5` for the
cheap per-note classification pass. The key is read from the `ANTHROPIC_API_KEY` environment
variable first, and failing that from a gitignored local file. It is never stored in the settings
asset, which lives in `ProjectSettings/` and would be committed.

The provider sits behind an interface from the start, so a second one can be added without
touching the drafting code.

## Why
Claude Code is already the first coding-agent adapter, so one key and account cover both halves of
the loop. An environment variable is the one place a key can live that no Unity serialisation path
picks up and commits by accident.

## Consequences
The capture MVP calls no LLM and needs no key. It ships the markdown export instead, which a
developer pastes into any agent themselves. Nothing here is load-bearing until spec drafting is
built.
