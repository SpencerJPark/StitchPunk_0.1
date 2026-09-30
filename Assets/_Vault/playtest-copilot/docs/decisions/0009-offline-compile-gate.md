# 0009: An offline compile gate, because the Editor is usually closed

Status: accepted (2026-09-29)

## Decision
`Packages/playtest-copilot/Tools~/compile-gate.sh` compiles the package's three assemblies with
the Roslyn that ships inside the Unity install, against the Editor's own reference assemblies, and
needs no Unity Editor running.

## Why
The package is built in a git worktree so the main checkout stays free for other work, and the
Editor is frequently closed. Without a gate the alternative is static review, which does not catch
a typo. This one caught a malformed character literal on its first run, and caught a wrong
overload on the main-toolbar attribute before that guess ever reached a worker's brief.

## Consequences
It is a floor, not a substitute: CS errors only, never an import setting, a bake, a serialisation
mismatch or anything needing the Editor. Two traps are baked into the script. First,
`Managed/UnityEngine/` already contains `UnityEditor.dll`, so referencing the copy one level up
makes every forwarded Editor type ambiguous with CS0433. Second, a worktree has no `Library`, so
the nunit and TestRunner assemblies are borrowed from the main checkout found via
`git worktree list`.
