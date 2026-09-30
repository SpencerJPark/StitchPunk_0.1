# 0002: Minimum Unity version is 6.4

Status: accepted (2026-09-28)

## Decision
Target Unity 6.4 and later only.

## Why
From 6.4, Entities, Collections, Mathematics and Entities Graphics ship with the Editor as core packages, and Unity is moving to a shared EntityId for GameObjects and entities. Supporting older Editors would mean extra fallback code for Entities 1.x and toolbar injection, for a shrinking audience.

## Consequences
One Entities code path. Older projects can still use a later export-only mode if that becomes a need.
