---
name: dots-test
description: Scaffold a Unity Test Runner fixture for the Stitch Punk project — an EditMode (pure-logic, no World) or PlayMode (World/ISystem integration) test class with the correct asmdef references already wired and the project conventions enforced (no `var`, explicit types, semantic names, NUnit `[Test]`/`[UnityTest]`). Use this skill whenever the user says "add a test for X", "write a unit test", "test the curve/scoring/grid math", "cover Y with tests", "add an EditMode/PlayMode test", "set up the test assembly", or asks to create anything under `Assets/_Scripts/Tests/`. Also use when deciding whether a given piece of logic belongs in an EditMode vs PlayMode fixture. Skip for: running the existing tests (that is Window ▸ General ▸ Test Runner, user-driven), or non-test gameplay code (use the other dots-* skills).
---

# dots-test

## What this skill does

Scaffolds a Unity Test Runner fixture for Stitch Punk and, if it does not exist yet, the test assembly that holds it. The project ships no game-specific tests by default, so the highest-value, lowest-friction coverage is a thin EditMode layer over the deterministic logic where a silent bug is expensive to find at runtime (AI curve evaluation, distance/range scoring, direction quantization, grid↔world conversions, blob-builder helpers).

## When to use it

- "Add a test for `FastDistanceScore`."
- "Cover the `ConsiderationBlob.Evaluate` curve with tests."
- "Write tests for the grid index math."
- "Set up a PlayMode test that spawns one unit and checks `WinnerSelectionSystem` picks flee."

Don't trigger for: running tests (user opens the Test Runner window), or editing gameplay code.

## EditMode vs PlayMode — decide first

This is the most important decision. Get it wrong and the test either can't compile or needs a World it doesn't have.

**EditMode (prefer this — no World, fast, runs headless-ish):** pure functions and value-type logic. A method qualifies if its inputs are plain values / structs you can construct directly (`float3`, `int2`, `LocalTransform.FromPosition(...)`, a `BlobAssetReference<T>` built with `BlobBuilder`). Examples: `AIUtils.FastDistanceScore` / `AttackRangeScore` / `IsTargetInRange`, `PathfindingUtils.*`, `DirectionUtils.Get4/6/8Direction`, `ConsiderationBlob.Evaluate`, `BlobLibraryUtils.BuildEnumLookup` / `EnumCount`.

**PlayMode (only when a World is unavoidable):** anything that takes a `ComponentLookup<T>`, `EntityManager`, `SystemAPI`, a `DynamicBuffer`, or runs an `ISystem`. You must create a `World`, an `EntityManager`, spawn entities, then `world.Update()` or call the system. Examples: `BehaviorQualifiers.Evaluate` (takes lookups), `WinnerSelectionSystem`, `BehaviorExecutionSystem`, `BehaviorInterruptSystem`, any awareness system.

Rule of thumb: if you'd need a live entity to call it, it's PlayMode. Otherwise EditMode.

## What to read first

1. `Assets/_Vault/Memories/Code/RULES.md` — conventions apply to test code too (no `var`, explicit types, semantic names).
2. The source file under test — pin the **actual** behaviour (characterization test), not what you assume it should do. If a mapping looks "wrong" (e.g. `Get4Direction((1,0)) == NorthEast`), it is intentional isometric quantization — assert the real value and add a comment.
3. For curve/blob logic: `Assets/_Scripts/Data/Structs/AIConfigBlobs.cs`.

## The test assembly

EditMode tests live under `Assets/_Scripts/Tests/` behind `StitchPunk.Tests.asmdef`. If it already exists, just add a fixture file next to the others. If creating it, copy this shape — the package GUIDs are lifted from `StitchPunk.Utils.asmdef` so every Unity package the gameplay code uses (Mathematics, Collections, Entities, Transforms, Burst) resolves; the last two name-references plus `precompiledReferences` wire NUnit + the Test Runner exactly as Unity's own generated test asmdef does:

```json
{
    "name": "StitchPunk.Tests",
    "references": [
        "GUID:f6d3133ab89495c40a933922769e2c5c",
        "GUID:7ff523084e6eb6a4bab4fb71b3b9fcb3",
        "GUID:8dfb72a3ac2144b4e9a720c0c1008aab",
        "GUID:734d92eba21c94caba915361bd5ac177",
        "GUID:2665a8d13d1b3f18800f46e256720795",
        "GUID:63afb046c8423dd448ae7aba042ea63d",
        "GUID:d8b63aba1907145bea998dd612889d6b",
        "GUID:e0cd26848372d4e5c891c569017e11f1",
        "GUID:a5baed0c9693541a5bd947d336ec7659",
        "GUID:0a82aeb665886483c867b7d137563619",
        "GUID:4d624505c28284c90a482e4d6ec34ada",
        "GUID:8819f35a0fc84499b990e90a4ca1911f",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "noEngineReferences": false
}
```

Local GUIDs: Utils `f6d3…`, Data `7ff5…`, Components `8dfb…`. The Stitch Punk gameplay code lives in the **global namespace** (empty `rootNamespace`), so a fixture in `namespace StitchPunk.Tests` still references `AIUtils`, `Direction`, `ConsiderationBlob`, etc. directly.

A PlayMode assembly is a **separate** folder + asmdef (drop `"includePlatforms": ["Editor"]` so it runs in players, add `Unity.Entities` to references) — do not mix EditMode and PlayMode fixtures in one assembly.

## Template — EditMode pure-function fixture

```csharp
using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    // Characterization tests: lock the CURRENT behaviour so a refactor can't silently change it.
    [TestFixture]
    public sealed class ExampleUtilsTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Method_DescribesTheExactBoundaryItPins()
        {
            float result = ExampleUtils.Score(new float3(0f, 0f, 0f), new float3(10f, 0f, 0f), 100f);
            Assert.AreEqual(0f, result, Tolerance);
        }
    }
}
```

## Template — EditMode blob fixture (BlobBuilder works without a World)

```csharp
BlobBuilder builder = new BlobBuilder(Allocator.Temp);
ref ConsiderationBlob root = ref builder.ConstructRoot<ConsiderationBlob>();
root.resolution = 3;
BlobBuilderArray<float> samples = builder.Allocate(ref root.samples, 3);
samples[0] = 0f; samples[1] = 0.5f; samples[2] = 1f;
BlobAssetReference<ConsiderationBlob> blob =
    builder.CreateBlobAssetReference<ConsiderationBlob>(Allocator.Persistent);
builder.Dispose();
try { Assert.AreEqual(0.25f, blob.Value.Evaluate(0.25f), 0.0001f); }
finally { blob.Dispose(); } // BlobAssetReference allocated Persistent MUST be disposed
```

## Template — PlayMode World fixture (only when a World is required)

```csharp
using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    [TestFixture]
    public sealed class WinnerSelectionSystemTests
    {
        private World testWorld;

        [SetUp]
        public void CreateWorld()
        {
            testWorld = new World("Test");
        }

        [TearDown]
        public void DisposeWorld()
        {
            testWorld.Dispose();
        }

        [Test]
        public void HigherPriorityOptionWins()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity();
            // entityManager.AddComponentData(unit, new StateMachine { ... });
            // entityManager.AddBuffer<UtilityActions>(unit) ... seed scored options ...
            // testWorld.GetOrCreateSystem<WinnerSelectionSystem>().Update(testWorld.Unmanaged);
            // Assert the chosen StateMachine.action.
        }
    }
}
```

## Conventions checklist (enforce in every fixture)

- Explicit types only — **no `var`**, even in tests. No single-letter names.
- `[TestFixture]` on the class (`sealed`), `[Test]` (or `[UnityTest]` for coroutine/frame-stepping) on methods.
- Float asserts use `Assert.AreEqual(expected, actual, tolerance)`.
- Dispose anything allocated `Persistent` (BlobAssetReference, NativeContainers) in a `finally` or `[TearDown]`.
- Pin **actual** behaviour with a comment when it's non-obvious; don't "correct" the code from a test.

## Verify

Tests are run by the user via **Window ▸ General ▸ Test Runner** (EditMode or PlayMode tab → Run All) — there is no headless runner wired up. After writing a fixture, confirm a clean compile with the Unity MCP `Unity_GetConsoleLogs` tool before handing off, then ask the user to run the suite.
