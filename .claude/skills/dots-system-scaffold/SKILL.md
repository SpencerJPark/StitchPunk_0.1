---
name: dots-system-scaffold
description: Conventions for a new ISystem + IJobEntity under Assets/_Scripts/Systems/ in Stitch Punk — folder-mirrors-group placement, group-level GameSceneTag gating, and when to drop [BurstCompile]. Use when creating a system file or fixing one that violates these.
---

# New system

General DOTS style (explicit types, no `var`, `[ReadOnly]` from `Unity.Collections`, `ScheduleParallel`
into `state.Dependency`, no managed allocation in a job) is in `_Vault/Memories/Code/RULES.md` — not repeated here.
Copy the shape of any current file in the target group folder.

## Placement

- **Pick the group from `Assets/_Scripts/Systems/SystemGroups.cs`** — it is the single ordering manifest
  and is self-documenting. Never infer the group from a stale list, and never declare a group inline.
- **The file goes in the folder named after its group.** The folder tree under `Systems/` *is* the group tree.
  `SystemPlacementConformanceTests` enforces this; an exemption needs an entry + reason in `PlacementExemptions`.

## The four things that are easy to get wrong

1. **Do not add `state.RequireForUpdate<GameSceneTag>()`.** Scene gating is group-level — top-level feature
   groups derive from `GameSceneSystemGroup` and gate once. Declare only your *data* requirements.
   ~73 existing systems still carry the legacy per-system call; do not copy them.
2. **`[BurstCompile]` is all-or-nothing.** If the system touches managed I/O (logging, `ScriptableObject`,
   `Debug.Log`), omit the attribute from the struct entirely rather than sprinkling it per-method.
3. **No empty `OnCreate` / `OnDestroy`.** Leave them out if there is nothing to do.
4. **Add the system's row to `_Vault/Memories/Code/Systems.md`** when you are done.

Burst log strings: only `G/g/D/d/X/x` format specifiers, no `+` concatenation, enums via
`EnumLogNames.Name()`, bools via `? 1 : 0`.
