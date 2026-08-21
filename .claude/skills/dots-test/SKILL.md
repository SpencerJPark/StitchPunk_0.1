---
name: dots-test
description: Scaffold a Unity Test Runner fixture for Stitch Punk — EditMode (pure logic, no World) or PlayMode (World/ISystem). Carries the project's test gotchas: gameplay code is in the global namespace, Persistent blobs must be disposed, and existing behaviour gets characterized rather than "fixed".
---

# Test fixtures

`Assets/_Scripts/Tests/` (EditMode, `StitchPunk.Tests.asmdef` — already wired, just add the file).
The Animation Toolkit has its own suites at `Packages/com.dotsanimationtoolkit/Tests/{EditMode,PlayMode}/`.

**Which kind:** needs a `World`, `EntityManager`, or `ComponentLookup` → PlayMode. Everything else
(scoring math, curves, direction quantization, grid math, blob helpers) → EditMode.

## Gotchas

- **Gameplay code is in the global namespace** (`rootNamespace` is empty), so a fixture declared in
  `namespace StitchPunk.Tests` still references `AIUtils`, `Direction`, `ConsiderationBlob` directly —
  no `using` needed, and adding one breaks it.
- **`BlobBuilder` works without a World**, so blob logic is EditMode-testable. Anything allocated
  `Allocator.Persistent` (a `BlobAssetReference`, a NativeContainer) **must** be disposed in a
  `finally` or `[TearDown]`, or the run leaks and later fixtures fail oddly.
- **Characterize, don't correct.** Pin the behaviour the code actually has. `Get4Direction((1,0)) ==
  NorthEast` is deliberate isometric quantization, not a bug — assert the real value and comment why.
- After a blob **struct layout** change the first run can fail spuriously in untouched fixtures.
  Recompile and re-run before debugging.

## Running

`mcp__UnityMCP__run_tests`, poll with `mcp__UnityMCP__get_test_job`. No headless CLI.
Confirm a clean compile via `mcp__UnityMCP__read_console` first.
