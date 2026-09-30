# 0005: Split agent tasks by asset ownership first, token budget second

Status: accepted (2026-09-28)

## Decision
In a batch of parallel tasks, each .unity, .prefab, .asset, .cs file (and each SubScene) that may be written is owned by exactly one task. Token budget (target under 100k, hard cap 150k) is applied within that constraint.

## Why
Unity scene and prefab YAML merge badly. A conflict costs more than a slightly larger task.
