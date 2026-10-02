# devloop — offline compile gate and convention lint

Checks this project's C# with **no Unity Editor running**, so a subagent or a worktree session can verify its own
edits. Works on all four embedded packages.

```
python Tools/devloop/devloop.py gate com.dotsanimationtoolkit      # whole package
python Tools/devloop/devloop.py gate DotsAnimationToolkit.Editor   # one assembly, ~5s
python Tools/devloop/devloop.py lint                               # the current git diff
python Tools/devloop/devloop.py lint Packages/com.dotsmovementtoolkit
python -m unittest discover -s Tools/devloop/tests -t Tools/devloop
```

Exit codes: **0** pass, **1** real errors or findings, **2** the check could not run honestly. Never read a 2 as a
pass — it means `GATE STALE` (Unity has not recompiled since a reference changed; reopen the Editor once) or no
Unity install was found (`set UNITY_DATA`).

## What it is and is not

The gate is a **floor**. It reports `error CS####` and nothing else: no Burst `BC####`, no ILPostProcessing, no
baking, no runtime behaviour. "Compile + rebake + play" is still the real verification pass. What the gate buys is
a 5-second answer with the Editor shut, in a place the Unity MCP tools cannot reach.

Measured on `com.dotsanimationtoolkit`, 2026-10-02: all six assemblies, 561 files, 0 errors, 93s cold for the whole
package (the first assembly spends ~50s loading analyzers; later single-assembly runs are 4–6s).

The lint covers only the five rules that can be checked mechanically. Everything visual — spacing, type scale,
tone, clipped text — belongs to `Tests/EditMode/EditorStyleConformanceTests.cs`, which measures resolved styles. A
lint that guessed at those would report things nobody will fix, and a tool people learn to ignore is worse than no
tool.

## How the gate works, and why it is built this way

Unity writes the exact Roslyn arguments it used to
`Library/Bee/artifacts/<hash>.dag/<Assembly>.rsp` — 315 references, ~40 defines and 18 analyzers including
`JobEntityGenerator`, `SystemGenerator.*` and `Unity.Entities.Analyzer`. The gate harvests that file and replaces
only the source list. So the real Entities source generators run, and the gate agrees with the Editor instead of
approximating it.

Four things here were each paid for with a debugging cycle. They look like details and are not:

- **Never construct the reference set by hand.** Referencing all 145 `Library/ScriptAssemblies/*.dll` plus every
  `Managed/UnityEngine/*.dll` yields phantom errors from two colliding `AABB` types. Real asmdefs reference 7–8
  assemblies, which is what the harvested rsp already encodes.
- **Keep the output assembly's filename.** Redirecting `-out:` to a different *name* changes the assembly identity,
  `InternalsVisibleTo("DotsAnimationToolkit.Editor")` stops matching, and 58 phantom `CS0122`s appear. Redirect the
  directory only.
- **Everything goes through a response file.** 315 references plus 561 sources overflow the Windows command line;
  it fails with "Argument list too long" and looks like a clean pass in 42ms. A suspiciously fast pass is a lie, so
  the gate flags a sub-second clean compile rather than trusting it.
- **Chain freshly built siblings.** Unity's rsp references sibling assemblies as prebuilt
  `Library/Bee/.../Runtime.ref.dll`. Left alone, gating `Editor` would compile against the *last Editor-built*
  Runtime — so editing Runtime and Editor together, the normal case here, could pass while Unity would fail. Each
  assembly's fresh `.ref.dll` is substituted for its siblings, and a dependent assembly whose upstream failed is
  reported `SKIPPED`, never quietly linked against the stale copy.

`Library/Bee` must exist, so the project has to have been compiled by the Editor at least once. Set `UNITY_DATA` to
override the Unity install path (default: `C:/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Data`).

## Layout

| File | Role |
|---|---|
| `devloop.py` | entry point; puts `Tools/devloop` on `sys.path` and calls `devloop_tools.cli` |
| `devloop_tools/cli.py` | `gate` / `lint` subcommands and the exit-code contract |
| `devloop_tools/packages.py` | assembly map for the four packages, in dependency order |
| `devloop_tools/rsp_harvest.py` | dag discovery, rsp harvest, source re-glob, sibling redirect, staleness |
| `devloop_tools/gate.py` | runs Roslyn, dedupes errors, formats the verdict |
| `devloop_tools/lint.py` | the five mechanical rules, diff-scoped |
| `tests/` | `unittest` fixtures (no pytest on this machine) |

Design decisions and the reasoning behind them: `Assets/_Vault/Tasks/NewPlans/DevLoopCli_Spec.md`.
