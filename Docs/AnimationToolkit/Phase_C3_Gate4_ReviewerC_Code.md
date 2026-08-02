# Reviewer C — Code Correctness — DOTS Animation Toolkit C3 Gate

STATUS: complete — VERDICT: PASS (no blocking items; 9 advisories)

Scope: `git diff 026a902..HEAD -- Packages/com.stitchpunk.dotsanimationtoolkit`
Files: Authoring/Baking/ActorBakeFailed.cs, ActorBaker.cs, AuthoringPathHash.cs,
AuthoringPathText.cs, RigBindingBakingSystem.cs, RigPartBakeLink.cs, RigTargetBaker.cs,
Authoring/Build/ClipRegistryBuilder.cs

Method: read shipped tree + real Unity Entities sources in Library/PackageCache. No compile/run.

---

## Findings

### P1 — Can `ActorBakeFailed` go stale across an incremental re-bake? — **NO. Verified against Entities sources. Not a defect.**

Label: **ADVISORY** (one residual gap, below, is advisory only).

Chain of evidence, all from `Library/PackageCache/com.unity.entities@e30ad8d00609`:

1. `Baker.AddComponent<T>(Entity)` → `AddComponent(Entity, ComponentType)`
   (`Unity.Entities.Hybrid/Baking/Baker.cs:1595-1607`):
   ```csharp
   public void AddComponent(Entity entity, ComponentType componentType)
   {
       if (_State.PrimaryEntity == entity)
       {
           AddDebugTrackingForComponent(entity, componentType);
           AddTrackingForComponent(componentType);
       }
       ...
   ```
   `MarkBakeFailed` (`ActorBaker.cs:130`) writes onto `GetEntity(TransformUsageFlags.None)`, which **is** the
   primary entity, so the branch is taken.

2. `AddTrackingForComponent` (`Baker.cs:1403-1407`) records the type index:
   `_State.BakerState->AddedComponents.Add(typeIndex);` — no special-casing of `[BakingType]`;
   `BakingOnlyTypeFlag` (`Unity.Entities/Types/TypeManager.cs:596`) only affects entity-scene stripping.

3. `BakerState.Revert` (`Unity.Entities.Hybrid/Baking/BakerState.cs:48-60`) removes every recorded type:
   ```csharp
   foreach (var typeIndex in AddedComponents)
       ecb.RemoveComponent(oldPrimaryEntity, ComponentType.FromTypeIndex(typeIndex));
   ```

4. `BakedEntityData.ApplyBakeInstructions` reverts **before** re-running
   (`BakedEntityData.cs:538-558`): "Revert as a single pass all the component bakers that are going to
   rebake" → `bakerState.Revert(revertEcb, entity, ...)` for every entry in `instructions.BakeComponents`.

5. Ordering is explicit (`BakedEntityData.cs:903-910`):
   ```csharp
   // The state reverts have to be applied before the state changes.
   revertEcb.Playback(_EntityManager);
   revertEcb.Dispose();
   ecb.Playback(_EntityManager);
   ```
   So the remove of `ActorBakeFailed` lands before the successful re-bake's `AddComponent(ClipRegistry)`.

6. Asset-dependency-triggered rebakes go through the same list, not a side path
   (`IncrementalBakingContext.cs:434-466`): `changedAuthoringObjectsIncludingDependencies` is iterated and
   each component is pushed into `_BakeInstructionsCache.BakeComponents`. So "user assigns the missing
   `RigAsset` on the `ClipSetAsset`" — which never touches the `ActorAuthoring` component — still reverts.

7. Destruction of the authoring component also reverts (`BakedEntityData.cs:440-453`,
   `instructions.RevertComponents`).

**Conclusion:** the tag cannot survive a re-run of `ActorBaker`. And the tag and `ClipRegistry` cannot
coexist, because all three `MarkBakeFailed()` call sites (`ActorBaker.cs:47`, `:64`, `:69`) `return`
before `AddComponent(actorEntity, new ClipRegistry ...)` at `ActorBaker.cs:75`. If `ActorBaker` does *not*
re-run, then neither the tag nor `ClipRegistry` changes — they stay consistent. The author's belief was
right; it is now verified.

**Residual gap (ADVISORY, A22-1):** the suppression is only sound while every bail-out both logs *and*
tags. `MarkBakeFailed` is a private method whose XML doc says "Every early return above must call this"
(`ActorBaker.cs:116-119`) — but nothing enforces it, which is precisely the failure mode `ActorBakeFailed`
was introduced to eliminate one level down. The tag makes `RigBindingBakingSystem`'s silence explicit but
re-creates the identical unenforced coupling inside `ActorBaker`. Cheap fix: fold the log into the helper
(`MarkBakeFailed(string message, Object context)`) so a bail-out physically cannot tag without logging.
Not blocking — all three current sites are correct.

---

### P2 — Did the `IBaker` refactor change the hash value? — **NO for `AuthoringPathHash.Of`. But the *phase* value DID change at the call site.** Split verdict.

#### P2a — `AuthoringPathHash.Of` itself: byte-for-byte identical input sequencing. **Not a defect.**

Old (`git show 026a902:.../AuthoringPathHash.cs`):
```csharp
uint pathHash = FnvOffsetBasis;
Transform currentNode = authoringTransform;
while (currentNode != null)
{
    string nodeName = currentNode.name;
    for (int characterIndex = 0; characterIndex < nodeName.Length; characterIndex++)
        pathHash = (pathHash ^ nodeName[characterIndex]) * FnvPrime;
    pathHash = (pathHash ^ (uint)currentNode.GetSiblingIndex()) * FnvPrime;
    pathHash = (pathHash ^ '/') * FnvPrime;
    currentNode = currentNode.parent;
}
```
New (`AuthoringPathHash.cs:64-87` + `:93-103`): same body, iterating
`[leaf] ++ baker.GetParents(leaf)`.

Equivalence, item by item:
- **Walk order.** `Baker.GetParents(GameObject, List<GameObject>)`
  (`com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/Baker.cs:613-628`):
  ```csharp
  parents.Clear();
  Transform parentTransform = gameObject.transform.parent;
  while (parentTransform != null)
  {
      parents.Add(parentTransform.gameObject);
      parentTransform = parentTransform.parent;
  }
  ```
  Immediate parent first, root last, leaf excluded — exactly the old `currentNode = currentNode.parent`
  chain minus the leaf. `CollectPathFromLeaf` (`AuthoringPathHash.cs:100-101`) prepends the leaf:
  `pathNodes.Add(leafGameObject); pathNodes.AddRange(ancestors);`. Identical sequence.
- **Names.** `Baker.GetName(GameObject)` (`Baker.cs:823-830`) is `string name = gameObject.name;` plus a
  dependency registration. `Transform.name` and `GameObject.name` are the same string. Identical.
- **Termination.** Both use Unity's overloaded `!= null` on a `Transform`, so both stop at the scene root
  and both treat a destroyed object the same way.
- **Constants.** `FnvOffsetBasis = 2166136261u`, `FnvPrime = 16777619u` — unchanged
  (`AuthoringPathHash.cs:54-55` vs. old lines).
- **Separator.** `(pathHash ^ '/') * FnvPrime` after the sibling index, per node, in both — including the
  trailing separator on the outermost ancestor. Unchanged.
- **Sibling index.** `(uint)pathNode.transform.GetSiblingIndex()` vs `(uint)currentNode.GetSiblingIndex()`
  — same value, same position in the sequence.
- **Overload resolution of `^`.** `pathHash ^ nodeName[i]` binds `uint ^ uint` in both (uint→int is not
  implicit, so the `int` candidate is inapplicable; `char`→`uint` is). No sign-extension difference.
- **Null input.** Old: loop body never entered → returns `FnvOffsetBasis`. New: explicit early return of
  `FnvOffsetBasis` (`AuthoringPathHash.cs:67-70`). Identical.

I found no input, including an empty name, a name with astral characters, or a root-level object, on which
the two implementations differ. **P2a passes.**

#### P2b — the derivation at the call site DID change. **ADVISORY (A18/A-4 impact, see also the A-4 ruling).**

`git show 026a902:.../ActorBaker.cs:485-486`:
```csharp
uint pathHash = AuthoringPathHash.Of(authoring.transform);
return (pathHash & 0x00FFFFFFu) * (1f / 16777216f);
```
HEAD (`ActorBaker.cs:542-543`):
```csharp
uint pathHash = AuthoringPathHash.Of(this, authoring.transform);
return (pathHash >> 8) * (1f / 16777216f);
```
So `SampleSettings.phase01` changes value for **every already-baked actor** in this diff. The stated
justification for that being harmless is `ActorBaker.cs:507-508`:

> "<c>RigBindingSystem</c> re-derives it per instance at spawn."

**That claim is false against the shipped tree.** `Packages/com.stitchpunk.dotsanimationtoolkit/Runtime/Systems/`
is an **empty directory**, and a repo-wide grep for `RigBindingSystem` returns only doc-comment mentions
(`ActorBaker.cs`, `ActorAuthoring.cs`, `RigBindingBakingSystem.cs`, `Runtime/Components/*.cs`) — no
definition. Nothing in `Runtime/` writes `SampleSettings` at all; `ClipSampler.SampleFrameIndex`
(`Runtime/Sampling/ClipSampler.cs:425-427`) consumes the baked `phase01` directly.

Consequence: today the baked value *is* the runtime value, so the mask→shift edit is an observable
data change on rebake, not a no-op behind a re-derivation. It is still benign (phase only spreads sampling
frames), but the paragraph justifying it rests on a system that does not exist. Same issue in
`RigBindingBakingSystem.cs:39-42`, where "`RigBindingSystem` rebuilds the buffer from the
`LinkedEntityGroup` at spawn, so the baked order never reaches a frame" is the sole argument for treating
`RigPartRef` order as unspecified — that argument is currently unbacked too.

**Recommendation:** reword both remarks to the future tense ("section 5.3's `RigBindingSystem` *will*…")
or mark them clearly as forward references. Advisory, not blocking — no wrong behaviour follows from it
inside this module's scope.

---

### P3 — `AuthoringPathText.TakeTrailingBytes` audit — **correct. No defect found.** (two ADVISORY nits)

**Capacity constant is right.** `AuthoringPathText.cs:46` `internal const int MaximumPathBytes = 125;`
matches `com.unity.collections@a43cabe808ca/Unity.Collections/FixedString.gen.cs:3766`:
```csharp
internal const ushort utf8MaxLengthInBytes = 125;
```

**Marker arithmetic — no off-by-one.** `AuthoringPathText.cs:76-81`:
```csharp
if (Encoding.UTF8.GetByteCount(fullPath) > MaximumPathBytes)
{
    fullPath = TruncationMarker + TakeTrailingBytes(
        fullPath,
        MaximumPathBytes - Encoding.UTF8.GetByteCount(TruncationMarker));
}
```
`".../"` is 4 ASCII bytes, so the budget passed down is 121 and the concatenation is ≤ 125 — exactly the
payload, not one over and not one wasted. The marker length is *measured*, not hardcoded, so changing the
marker keeps the sum right. Capacity-smaller-than-marker is unreachable at 125/4; were it forced negative,
`TakeTrailingBytes` breaks on its first iteration (`0 + characterBytes > negative`), returns `""`, and the
result is the bare marker — degrades, never throws. Correct on both counts.

**Surrogate handling — correct; no pair and no multi-byte UTF-8 sequence can be split.**
`AuthoringPathText.cs:103-118`:
```csharp
while (startIndex > 0)
{
    int candidateIndex = startIndex - 1;
    if (candidateIndex > 0 && char.IsLowSurrogate(text[candidateIndex]))
        candidateIndex--;
    int characterBytes = Encoding.UTF8.GetByteCount(
        text.ToCharArray(candidateIndex, startIndex - candidateIndex));
    if (usedBytes + characterBytes > byteBudget) break;
    usedBytes += characterBytes;
    startIndex = candidateIndex;
}
return text.Substring(startIndex);
```
- `startIndex` is only ever assigned `candidateIndex`, which is always a code-point start, so the retained
  boundary is always a code-point boundary. Pairs are consumed atomically (2 units, 4 bytes).
- Invariant check: after consuming a pair at `(k-1, k)`, `startIndex = k-1` — the *high* surrogate index,
  not `k` — so `text[startIndex]` is never a lone trailing surrogate on a later iteration. No path leaves
  a dangling half.
- The budget is measured in **bytes** via `Encoding.UTF8.GetByteCount`, never in chars — the class-doc's
  own warning (`:26-28`) is actually obeyed by the code, which is the bug that previously shipped.
- The whole function operates on UTF-16 units and then re-encodes; it never indexes into a UTF-8 buffer,
  so splitting a multi-byte UTF-8 sequence is structurally impossible here.

**`candidateIndex > 0` is correct, and the boundary case is benign.** When `candidateIndex == 0` and
`text[0]` is a lone low surrogate, the guard correctly declines to step to `-1`. The 1-char slice is then
measured (3 bytes, replacement) and `startIndex` reaches 0, so the whole string is returned — reachable
only when the whole string already fits. No `IndexOutOfRangeException`, no infinite loop (`startIndex`
strictly decreases every non-breaking iteration since `candidateIndex < startIndex` always).
Minor imprecision, not a bug: the code does not verify `text[candidateIndex-1]` is a *high* surrogate
before pairing, so an unpaired low surrogate preceded by a BMP char groups two units into one step. That
is conservative (measures correctly, retains slightly more atomically), never incorrect.

**Encoder-divergence check (the thing that could have made the byte budget a lie).**
`CopyFromTruncated` (`Unity.Collections/FixedStringAppendMethods.cs:411-427`) →
`UTF8ArrayUnsafeUtility.Copy` (`Unity.Collections/UTF8ArrayUnsafeUtility.cs:22-28`) →
`Unicode.Utf16ToUtf8` (`Unity.Collections/Unicode.cs:628-638`), which converts **rune by rune** and
returns `Overflow` before writing a partial rune — so the fallback truncation also lands on a code-point
boundary, never mid-sequence. For lone surrogates, `Unicode.Utf16ToUcs` (`Unicode.cs:475-488`) emits the
raw surrogate value as the rune, which `UcsToUtf8` encodes in 3 bytes; .NET's `Encoding.UTF8.GetByteCount`
substitutes U+FFFD, also 3 bytes. **The two encoders agree on the count**, so `RenderPath`'s .NET-measured
budget cannot under-count what Unity will write. This was the most plausible remaining trap and it is
clean.

**ADVISORY P3-a:** `MaximumPathBytes = 125` is a hand-copied literal. `FixedString128Bytes.UTF8MaxLengthInBytes`
is public (`FixedString.gen.cs:3777`) but not a compile-time constant, so it cannot be a `const`. Either make
it `internal static readonly int MaximumPathBytes = FixedString128Bytes.UTF8MaxLengthInBytes;` or add a
one-line EditMode assertion that the two are equal, so a Collections change cannot silently make the
budget wrong.

**ADVISORY P3-b:** `text.ToCharArray(candidateIndex, startIndex - candidateIndex)`
(`AuthoringPathText.cs:111`) allocates a `char[]` **per retained character** — O(path length) garbage per
call, on a path this walks for every rig part in the scene. Managed baker code, so it is legal, just
wasteful. `Encoding.UTF8.GetByteCount(char[], int, int)` over `text.ToCharArray()` hoisted once, or a
manual code-point width switch, removes it. Not blocking.

---

### P4 — `ComponentLookup<ActorBakeFailed>.HasComponent` on a zero-sized tag inside `[BurstCompile] IJobEntity` — **legal and correct. Not a defect.**

Call site: `RigBindingBakingSystem.cs:151` `if (actorBakeFailedLookup.HasComponent(bakeLink.actorRoot))`,
inside `[BurstCompile] internal partial struct ResolveRigPartBindingsJob`.

1. **Constraint is satisfied.** `ComponentLookup<T>` is declared
   `public unsafe struct ComponentLookup<T> where T : unmanaged, IComponentData`
   (`com.unity.entities@e30ad8d00609/Unity.Entities/Iterators/ComponentLookup.cs:45`).
   `ActorBakeFailed` (`ActorBakeFailed.cs:34-37`) is an empty `struct : IComponentData` — unmanaged, and
   an empty struct is a legal type argument.

2. **`HasComponent` never touches component *data*, only archetype membership** — so a zero size cannot
   matter. `ComponentLookup.cs:140` → `:151-158`:
   ```csharp
   var ecs = m_Access->EntityComponentStore;
   return ecs->HasComponent(entity, m_TypeIndex, ref m_Cache, out entityExists);
   ```
   → `Unity.Entities/EntityComponentStore.cs:1325-1335`:
   ```csharp
   entityExists = Exists(entity);
   if (Hint.Unlikely(!entityExists)) return false;
   var archetype = GetArchetype(entity);
   if (Hint.Unlikely(archetype != cache.Archetype)) cache.Update(archetype, type);
   return cache.IndexInArchetype != -1;
   ```
   Only `IndexInArchetype` is read.

3. **`LookupCache.Update` explicitly tolerates a zero-sized / absent type**
   (`EntityComponentStore.cs:3183-3189`):
   ```csharp
   ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex, ref IndexInArchetype);
   ComponentOffset = IndexInArchetype == -1 ? 0 : archetype->Offsets[IndexInArchetype];
   ComponentSizeOf = IndexInArchetype == -1 ? (ushort)0 : archetype->SizeOfs[IndexInArchetype];
   ```
   No assert, no division by size, no data pointer. A zero-sized type simply has `SizeOfs == 0`, which is
   never used by this path.

4. **Burst-legal:** the whole call chain is unmanaged pointer arithmetic on `EntityDataAccess*` /
   `Archetype*`; the only conditional code is behind `ENABLE_UNITY_COLLECTIONS_CHECKS`
   (`ComponentLookup.cs:153-155`, `AtomicSafetyHandle.CheckReadAndThrow`), which Burst supports. Nothing
   managed is reached. `[BakingType]` is only a `TypeManager.BakingOnlyTypeFlag`
   (`Unity.Entities/Types/TypeManager.cs:596`, set at `:3536`) consumed by entity-scene stripping — it has
   no effect on `TypeIndex` resolution or on `ComponentLookup`.

5. **The indexer is never used on this lookup** — only `HasComponent`. (Using
   `actorBakeFailedLookup[entity]` on a zero-sized type is the pattern that would be questionable; the
   code does not do it.) Verified by reading the whole job (`RigBindingBakingSystem.cs:131-194`).

6. **`Entity.Null` is safe input.** `HasComponent` starts with `entityExists = Exists(entity)` and
   returns `false` for a null/dead entity rather than dereferencing. (In practice `bakeLink.actorRoot` is
   never `Entity.Null` — it is only written at `RigTargetBaker.cs:101` from
   `GetEntity(actorAuthoring, TransformUsageFlags.Dynamic)`, and the branch that omits it also omits the
   whole `RigPartBakeLink`, so the job never sees such a part.)

**Bonus verification on the same job — Burst string legality.** The four `Debug.LogError($"…")` calls
(`RigBindingBakingSystem.cs:155, 172, 183`) interpolate `bakeLink.authoringPath`, a
`FixedString128Bytes` (`RigPartBakeLink.cs:55`). Burst is pinned at **1.8.29**
(`Library/PackageCache/com.unity.burst@6bb9aca3ef38/package.json:4`), and its `CHANGELOG.md:150` records
under **[1.8.21]**: *"Fixed internal compiler error when using a `FixedStringNBytes` value in an
interpolated string"* — so the construct is supported on the pinned version, not merely tolerated. No `+`
concatenation (BC1016) and no format specifiers at all (so BC1343 cannot apply). `bakeLink.targetId` is
`uint` (`RigPartBakeLink.cs:35`), covered by `BurstString.Format(byte*, ref int, int, uint, int)`
(`com.unity.burst@6bb9aca3ef38/Runtime/BurstString.cs:258`). Clean.

---

## General correctness sweep

### S1 — `Baker.GetComponentsInChildren<T>()` include-inactive claim — **verified true.** (no finding)

`ActorBaker.cs:382-388` asserts the API includes inactive children by default and has no `bool` overload.
This is the exact class of claim that previously shipped as a bug, so I checked it:
`com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/Baker.cs:31`
```csharp
private const bool kDefaultIncludeInactive = true;
```
`Baker.cs:490-492`
```csharp
public T[] GetComponentsInChildren<T>(GameObject gameObject) where T : Component
{
    var components = gameObject.GetComponentsInChildren<T>(kDefaultIncludeInactive);
```
The overload set is `()`, `(Component)`, `(GameObject)` plus three `List<T>` variants — **no `bool`
parameter anywhere**. The comment is accurate on both counts.

### S2 — `CaptureRestPose` switching to `GetComponent<Transform>(authoring)` — **verified real bug fix.** (no finding)

`RigTargetBaker.cs:221` `Transform partTransform = GetComponent<Transform>(authoring);` (was
`authoring.transform`). `Baker.GetComponentInternal<T>` (`Baker.cs:122-132`):
```csharp
var hasComponent = gameObject.TryGetComponent<T>(out var returnedComponent);
_State.Dependencies->DependOnGetComponent(gameObject.GetEntityId(), TypeManager.GetTypeIndex<T>(),
    hasComponent ? returnedComponent.GetEntityId() : EntityId.None, ...);
// Transform component takes an implicit dependency on the entire parent hierarchy
var transform = returnedComponent as Transform;
if (transform != null)
    _State.Dependencies->DependOnParentTransformHierarchy(transform);
```
`DependOnGetComponent` → `AddObjectReference(returnedComponent)` (`BakeDependencies.cs:589-595`,
`:448-451`), which is what puts the Transform into the changed-object propagation set — so moving the part
now retriggers the baker. The XML comment's parenthetical about the parent-hierarchy over-invalidation is
also literally what `BakeDependencies.cs:653-673` does (it hashes the ancestor **ids** and adds object
references for them). Every API claim in that doc block checks out.

### S3 — **ADVISORY** — `RigPartBinding` written on the failure path of `RigTargetBaker` but `RigPartBakeLink` withheld — consistent, no defect

`RigTargetBaker.cs:75-96`: `RigPartBinding{ actorRoot = Entity.Null, targetIndex = -1 }` is added
unconditionally, `RigPartBakeLink` only in the `else`. `ResolveRigPartBindingsJob.Execute` requires
`in RigPartBakeLink` (`RigBindingBakingSystem.cs:131`), so such a part is never visited and its
`targetIndex` stays −1. On an incremental re-bake the `BakerState.Revert` proved in P1 removes the stale
`RigPartBakeLink` when a previously-valid id becomes invalid. Correct in both directions.

### S4 — **ADVISORY** — a failed actor's entity now always survives baking (behavioural change)

`MarkBakeFailed` (`ActorBaker.cs:130`) calls `GetEntity(TransformUsageFlags.None)`. Requesting `None` is
**not** the same as requesting nothing — `TransformUsageFlagCounters.Add`
(`Unity.Entities.Hybrid/Baking/TransformUsageFlags.cs:146-151`) increments `IsUsed` for any request
including `None`, and the type's own doc (`:204-209`) says:
> "TransformUsage.None is different from Unused … IsUnused means that there is no valid reference to this
> entity, hence the entity shouldn't exist in the game world."

`UpdateReferencedEntitiesJob` (`BakedEntityData.cs:960-974`) only flags `RemoveUnusedEntityInBake` when
`usage.IsUnused`. So the design comment ("this baker needs the entity to exist so it can write the tag")
is correct — but the side effect is that a failed actor with **no** parts referencing it, which previously
would have been stripped as unused, now emits a bare entity into the built scene. Harmless (the bake has
already logged an error, and `ActorBakeFailed` itself is stripped as a `[BakingType]`), but it is a real
change nobody documented. Worth one sentence in the CHANGELOG.

Also confirmed non-hazardous: `RemoveEntityInBakeDestroyEntitiesSystem`
(`Unity.Entities.Hybrid/Baking/RemoveEntityInBakeDestroyEntitiesSystem.cs:6`) is
`[WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]`, i.e. it runs **after** baking — so
it can never destroy an actor entity out from under `RigBindingBakingSystem` in `PostBakingSystemGroup`
and produce a spurious "no earlier message explained why".

### S5 — no ECB / structural-change hazard in the baking system (no finding)

`RigBindingBakingSystem.OnUpdate` (`:65-81`) schedules `ClearRigPartRefsJob.ScheduleParallel` then
`ResolveRigPartBindingsJob.Schedule`, both threading `state.Dependency` — clear-then-rebuild is correctly
sequenced, and the resolve pass is single-threaded so the cross-entity `BufferLookup<RigPartRef>` append
and the duplicate scan that reads the same buffer cannot race. No ECB, no structural change, no
`.Run()`. Rule-conformant.

`[WithAll(typeof(ClipRegistry))]` on the clear job cannot leave a stale buffer: `AddBuffer<RigPartRef>`
(`ActorBaker.cs:83`) and `AddComponent(ClipRegistry)` (`:75`) are on the same success path, and both are
recorded in `BakerState.AddedComponents`, so a later failing re-bake reverts both together.

The short-circuit at `RigBindingBakingSystem.cs:136-138` is correctly ordered — `clipRegistryLookup[…]`
on line 138 is only evaluated after `HasComponent` on line 136 returns true.

### S6 — **ADVISORY** — diagnostic-granularity regression in the merged bail-out

Pre-diff, `ClipRegistry.Value.IsCreated == false` had its own message ("clip registry failed to build").
It is now folded into the generic branch (`RigBindingBakingSystem.cs:136-156`), so if it ever occurred
without the tag the user would be told "no earlier message explained why … this is a toolkit defect".
That is arguably the right message, and the state looks unreachable — `ActorBaker.cs:75` only adds
`ClipRegistry` when `TryAcquireRegistry` returned true, which yields either a store hit or a freshly built
blob, both `IsCreated`. Noted, not a defect.

### S7 — **ADVISORY** — the duplicate-target diagnostic fires once per duplicate

`RigBindingBakingSystem.cs:177-185`: with N parts claiming one target, N−1 errors are logged. Intentional
(each duplicate is named and skipped), and consistent with the A22 rationale of naming the offending
object. Flagging only because the amendment's stated goal was to stop N-fold restatements elsewhere.

### S8 — **ADVISORY** — malformed XML documentation in `ClipRegistryBuilder`

`ClipRegistryBuilder.cs:63-79`:
```
/// <remarks>
/// The store-hit short-circuit … the seam that makes the difference assertable.
/// </para>          <-- closing tag with no opener
/// <para>
```
Line 71 closes a `<para>` that was never opened, and the member then carries **two** consecutive
`<remarks>` elements (`:63-79` and `:80-96`). This is badly-formed doc XML (CS1570 / CS1587 territory
whenever a consumer compiles with `/doc`, which Unity does not by default — so it will not show up in this
project's Console). For a package sold with `Documentation~`, worth fixing: delete the stray `</para>`,
wrap the first paragraph in `<para>`, and merge the two `<remarks>` into one.

### S9 — **ADVISORY** — `BuildInvocationCount` doesn't count what its summary says

`ClipRegistryBuilder.cs:61` — *"Number of times `Build` has allocated a persistent blob this session."*
`:152-155`:
```csharp
#if UNITY_EDITOR
System.Threading.Interlocked.Increment(ref buildInvocationCount);
#endif
registry = BuildValidatedBlob(clipSet, Allocator.Persistent);
```
The increment precedes the allocation, so any throw out of `BuildValidatedBlob` over-counts. Trivially
fixable by moving the increment one line down; as a test seam it makes no practical difference.

### S10 — no leak on the validation-failure path (no finding)

`ActorBaker.TryAcquireRegistry` (`:195-206`) catches `ClipValidationException` and sets `registry = default`
without disposing. That is safe: the only `throw new ClipValidationException` in the package is
`ClipRegistryBuilder.cs:239`, inside `ValidateForBakeOrThrow`, which runs at `:150` — **before** the
`Allocator.Persistent` allocation at `:155`. Nothing can be allocated and then abandoned.
`TryComputeContentHash` (`:210-219`) disposes its `Allocator.Temp` probe blob in a `finally`, correctly
covering a throw from `HashRegistry`/`ComposeDedupKey`.

### S11 — project hard-rules conformance across the eight files (no finding)

- No `var` — grep over `Authoring/Baking/*.cs` + `Authoring/Build/ClipRegistryBuilder.cs` returns nothing.
- No single-letter identifiers — grep returns nothing.
- No `.Run()` — grep returns nothing; both jobs use `ScheduleParallel`/`Schedule` assigned to
  `state.Dependency` (`RigBindingBakingSystem.cs:72, 80`).
- `[ReadOnly]` (`RigBindingBakingSystem.cs:125, 127`) resolves to `Unity.Collections.ReadOnlyAttribute`;
  `using Unity.Collections;` is present at `:4`. The only `ReadOnlyAttribute` in the Entities tree is in
  `Unity.Entities/SourceGenerators/Source~/Mock/Unity.Entities.Mock/EntitiesMock.cs:734`, which is not a
  compiled assembly here, so there is no CS0104 ambiguity.
- No managed allocation inside the Burst job — `ResolveRigPartBindingsJob` touches only entity data and
  `FixedString128Bytes`.
- Burst log strings — no format specifiers, no `+` concatenation. See P4.
- `[DisallowMultipleComponent]` on both `ActorAuthoring` (`ActorAuthoring.cs:24`) and `RigTargetAuthoring`
  (`RigTargetAuthoring.cs:21`), so the `BakerDebugState` duplicate-add error cannot be triggered by two
  bakers of the same type writing `ActorBakeFailed` / `ClipRegistry` to one entity.
- `InternalsVisibleTo` for both test assemblies is present (`Authoring/AssemblyInfo.cs:8-10`), and the
  PlayMode test asmdef is `"includePlatforms": ["Editor"]`, so the unguarded
  `ClipRegistryBuilder.BuildInvocationCount` use in `Tests/PlayMode/ActorBakingAcceptanceTests.cs:761`
  compiles under the `#if UNITY_EDITOR` guard.
- No dangling references to the removed `RigPartBakeLink.authoringPathHash` field anywhere in the package.

---

## A-4 ruling — should `(pathHash >> 8)` stay?

**Recommendation: keep the shift, but cut the comment down to two lines, and fix the doc claim it leans on.**

Reasoning, taking the author's premise as given (I did not attempt the test, per instruction):
1. The shift is not *worse* than the mask on any input. Both take 24 bits of a 32-bit FNV-1a value and
   scale by 2⁻²⁴; `pathHash >> 8` is at most `0x00FFFFFF`, so `phase01 ∈ [0, 1)` holds identically. There
   is no correctness argument for reverting.
2. The author's "no discriminating test is constructible" claim is *not* the same as "the choice is
   arbitrary". It says the leaf-first walk makes the two derivations indistinguishable **for the inputs
   this package generates**. But `AuthoringPathHash.Of` is a general helper on a shipped path — a future
   caller that hashes root-first, or that mixes a discriminator last, would land exactly in the case where
   the low byte is least mixed. The shift is the derivation that stays correct under that change; the mask
   is the one that quietly degrades. That is a real, if unobservable-today, reason to keep it.
3. So: keeping an unobservable micro-improvement is fine. What is **not** fine is a 14-line comment
   (`ActorBaker.cs:524-541`) that shouts "NO TEST COVERS THE SHIFT, and none can — do not write one" and
   narrates the deletion of a fixture at a prior gate. That is review correspondence, not code
   documentation; it will read as a defect to the first customer who opens the file. Replace with roughly:
   *"Bits 8–31: FNV-1a ends in a multiply, so the low byte is the least-mixed. The remaining 24 bits are
   exactly the width the phase needs. Unobservable for the leaf-first walk above; kept because it stays
   correct if the walk order ever changes."*
4. **Fix the load-bearing false claim first** — see P2b. The shift *does* change baked `phase01` for
   existing users, and the sentence that makes that harmless ("`RigBindingSystem` re-derives it per
   instance at spawn", `ActorBaker.cs:507-508`) describes a system that does not exist in
   `Runtime/Systems/`. Either land that system or reword the remark. This matters more than the shift
   itself.

---

## VERDICT

**PASS**

I found no blocking defect. All four priority items came back clean against the real sources, and the two
riskiest — the `[BakingType]` staleness question and the surrogate/byte arithmetic — are not merely
"probably fine" but demonstrably correct: `BakerState.Revert` removes every baker-added type by index and
`revertEcb` plays back before the baker ECB, so `ActorBakeFailed` cannot outlive a re-run of `ActorBaker`
under any trigger including an asset-only dependency change; and `TakeTrailingBytes` never splits a
surrogate pair or a UTF-8 sequence, with `.NET`'s and Unity's encoders agreeing on byte counts even for
lone surrogates, so the budget cannot under-count. The `IBaker` refactor of `AuthoringPathHash.Of` is
byte-for-byte input-equivalent to the pre-diff walk. `ComponentLookup<ActorBakeFailed>.HasComponent` reads
only `LookupCache.IndexInArchetype` and is Burst-legal for a zero-sized tag. The two API claims this
package has historically gotten wrong by recall — `GetComponentsInChildren` include-inactive, and the
`FixedString` byte budget — are both now stated correctly and both check out against `PackageCache`. The
`GetComponent<Transform>` change is a genuine incremental-baking bug fix, not cosmetics. What is left is
documentation drift, not behaviour.

### Blocking items
*(none)*

### Advisory items, in priority order
1. **P2b / A-4** — `ActorBaker.cs:507-508` and `RigBindingBakingSystem.cs:39-42` justify a real data
   change and a real determinism non-guarantee by citing `RigBindingSystem`, which does not exist:
   `Runtime/Systems/` is an empty directory and the name appears only in doc comments. Reword to a forward
   reference or land the system. Highest-value fix here.
2. **A22-1 (P1 residual)** — `MarkBakeFailed` can be called without logging; nothing enforces the pairing
   the tag exists to make explicit. Fold the log into the helper.
3. **S4** — a failed actor's entity now always survives baking (`TransformUsageFlags.None` marks it
   *used*, not unused). Behavioural change; one CHANGELOG line.
4. **A-4 comment** — trim `ActorBaker.cs:524-541` from gate correspondence to two lines of rationale.
5. **S8** — malformed doc XML at `ClipRegistryBuilder.cs:71` (stray `</para>`) plus duplicate `<remarks>`.
6. **P3-a** — `MaximumPathBytes = 125` is a hand-copied literal; bind it to
   `FixedString128Bytes.UTF8MaxLengthInBytes` or assert equality in an EditMode test.
7. **P3-b** — `text.ToCharArray(...)` allocates per retained character in `TakeTrailingBytes`.
8. **S9** — `BuildInvocationCount` increments before the allocation it claims to count.
9. **S6 / S7** — noted diagnostic-granularity observations; no action required.

### Priority-item answers
1. **Can `ActorBakeFailed` go stale? — No.** `Baker.AddComponent` records it in
   `BakerState.AddedComponents` (`Baker.cs:1595-1607`, `:1403-1407`); `BakerState.Revert`
   (`BakerState.cs:53-60`) removes every recorded type; `BakedEntityData.cs:538-558` reverts every entry
   in `instructions.BakeComponents` **before** re-baking; `BakedEntityData.cs:903-910` plays `revertEcb`
   back before the baker `ecb` ("The state reverts have to be applied before the state changes.");
   asset-dependency-triggered rebakes enter the same list (`IncrementalBakingContext.cs:434-466`). There
   is no sequence in which the tag survives a successful re-bake. If `ActorBaker` does not re-run, neither
   the tag nor `ClipRegistry` changes, so they stay consistent. Verified, not assumed.
2. **Did the hash value change? — No for `AuthoringPathHash.Of`.** `Baker.GetParents`
   (`Baker.cs:613-628`) returns parent-first-to-root excluding the leaf, which `CollectPathFromLeaf`
   prepends the leaf to — the exact old walk. `Baker.GetName` (`:823-830`) is `gameObject.name`.
   Constants, separator, sibling index, `^` overload resolution and null handling are all identical.
   **But the call site changed**: `(pathHash & 0x00FFFFFFu)` → `(pathHash >> 8)`, so `SampleSettings.phase01`
   does change for existing users — see P2b.
3. **`TakeTrailingBytes` — correct.** No surrogate pair or multi-byte UTF-8 sequence can be split;
   `candidateIndex > 0` correctly guards index −1 and its one edge case is unreachable-or-benign; the
   `.../` arithmetic (`125 − 4 = 121`) is exact with no off-by-one and degrades to the bare marker if the
   budget were ever forced negative; `Unicode.Utf16ToUtf8` truncates on rune boundaries so the
   `CopyFromTruncated` backstop is also safe.
4. **`ComponentLookup<ActorBakeFailed>.HasComponent` under Burst — legal and correct.** It resolves to
   `EntityComponentStore.HasComponent` (`:1325-1335`) which reads only `LookupCache.IndexInArchetype`;
   `LookupCache.Update` (`:3183-3189`) explicitly tolerates size 0 and absence. Pure unmanaged code, no
   managed reach, `[BakingType]` has no bearing. The indexer is never used on this lookup, and
   `Entity.Null` input returns `false` rather than faulting.
5. **A-4 — keep the shift**, trim the comment to two lines, and fix the `RigBindingSystem` claim it leans
   on (item 1 above). Full reasoning in the A-4 section.

STATUS: complete


