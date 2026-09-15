# The Events Tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator ▸ Events** — after Cutscene
Director in the tab strip. Three columns: the project's event registry on the
left, the selected event's fields in the middle, and its routes on the right.

---

## Keys

The left column is the project event registry, shown as a searchable catalog.
Each row's first line is the event name; the second reads either
`maskable · key 16` or `pulse-only · key 80`. Keys 16 through 79 are
maskable — there are 64 of them, each owns a bit in `AnimEventMask`, and each
can hold a window. Keys 80 and up are pulse-only: they still carry a payload
through `AnimEventOutput`, but they never claim a mask bit and can't be
tested with `AnimEventMaskKeys.IsOpen`.

A budget line under the search field reads "n of 64 maskable keys used ·
m pulse-only" and turns amber once the maskable range is full.

- **New** mints the lowest free maskable key. Once all 64 maskable keys are
  taken, it mints a pulse-only key instead — you never get a hard stop.
- **Refresh** rescans clips, cutscenes and profiles for key usage.
- Right-click a row for:
  - **Rename** — inline, in place.
  - **Delete** — confirms with a count of where the key is used; a key that
    is actually in use asks a second time before it goes.
  - **Generate Constants** — rewrites the event constants file from the
    current registry.
  - **Merge into…** — moves every marker using this key onto another key and
    removes this one. This acts on the project registry, not one clip.

## Event fields and usage

The middle column describes whichever key is selected:

- **Name** and **Description**.
- **Default window frames** — the window length new markers for this key
  start with.
- The payload schema: **Int param label**, **Int value names** (comma
  separated), **Float param label**, **Float unit**.
- A **marker preview** showing how a marker carrying this key's payload
  fields will actually look on a track.
- A **preview clip**, played while you scrub the field values.
- **Used by** — counts of clips, cutscenes and profiles referencing the key,
  each with a button that pings the asset in the Project window.

## Routes and the routing asset

The right column lists the routes on the selected key. A route has:

- **Kind** — `Sound`, `Vfx`, `Ragdoll`, `ShaderView` or `Custom`.
- **Route id** — the host's own id for whatever the kind points at, shown in
  hex: a sound enum value, a prefab hash, a material view index.
- **Note** and **Display asset** — editor convenience only, never baked.

**Route** adds one. Below the list sit a **System name** field and
**Generate consumer stub…**, covered below.

Routes live in `AnimEventRoutingAsset`, a `ScriptableObject` holding a single
`List<AnimEventRoute> routes`. Opening the Events tab never creates this
asset. The first route you add creates it at
`Assets/Generated/DotsAnimationToolkit/AnimEventRouting.asset`, the folder the
package already writes generated constants into; if one already exists
anywhere under Assets, that one is used instead, so you can move it wherever
you like. It lives in Assets, not ProjectSettings, because it gets baked —
there is one per project.

## Baking routes

Add **DOTS Animation Toolkit/Anim Event Routing** to one GameObject in a
subscene. The authoring component, `AnimEventRoutingAuthoring`, has a single
field, `routing`, pointing at the `AnimEventRoutingAsset`.
`AnimEventRoutingBaker` bakes it through `AnimEventRoutingBuilder` into a
singleton:

```csharp
public struct AnimEventRouting : IComponentData
{
    public BlobAssetReference<AnimEventRoutingBlob> Value;
}
```

The blob holds `routes` (a `BlobArray<AnimEventRouteBlob>`, each entry
`{eventKey, kind, routeId}`), plus `keys` and `keyStarts` for lookup. Entries
are sorted by key, then kind, then route id at bake time, so the same routes
authored in any order produce an identical blob.

## Reading routes from a system

```csharp
[BurstCompile]
public static bool TryGetRoutes(
    ref AnimEventRoutingBlob routing,
    uint eventKey,
    out int routeStart,
    out int routeCount)
```

`AnimEventRoutingApi.TryGetRoutes` is a Burst-compiled binary search over
`keys`/`keyStarts`. Pass the blob by `ref`, not by value:

```csharp
ref AnimEventRoutingBlob routingBlob = ref routing.Value.Value;
if (AnimEventRoutingApi.TryGetRoutes(ref routingBlob, eventKey, out int routeStart, out int routeCount))
{
    for (int routeIndex = routeStart; routeIndex < routeStart + routeCount; routeIndex++)
    {
        AnimEventRouteBlob route = routingBlob.routes[routeIndex];
        // route.kind, route.routeId
    }
}
```

A short sample, over pending events rather than one known key:

```csharp
[BurstCompile]
[WithAll(typeof(AnimEventsPending))]
public partial struct DispatchRoutedEventsJob : IJobEntity
{
    [ReadOnly]
    public BlobAssetReference<AnimEventRoutingBlob> RoutingReference;

    private void Execute(in DynamicBuffer<AnimEventOutput> animEvents)
    {
        ref AnimEventRoutingBlob routingBlob = ref RoutingReference.Value;
        for (int eventIndex = 0; eventIndex < animEvents.Length; eventIndex++)
        {
            AnimEventOutput animEvent = animEvents[eventIndex];
            if (!AnimEventRoutingApi.TryGetRoutes(ref routingBlob, animEvent.eventKey, out int routeStart, out int routeCount))
            {
                continue;
            }
            for (int routeIndex = routeStart; routeIndex < routeStart + routeCount; routeIndex++)
            {
                AnimEventRouteBlob route = routingBlob.routes[routeIndex];
                // dispatch on route.kind
            }
        }
    }
}
```

## Generating a consumer stub

**Generate consumer stub…** asks for a folder inside Assets — it remembers
the last one you picked — then writes `<Name>AnimEventSystem.cs`: a
`partial struct <Name>AnimEventSystem : ISystem` that schedules an
`IJobEntity` over `AnimEventOutput`, gated on `AnimEventsPending`, with a
`switch` over `AnimEventRouteKind` covering the kinds your routes actually
use (all five kinds when the key has none yet). Each case gets a `// TODO`.

The stub is your code from the moment it's written: edit it freely.
Regenerating it overwrites it, so move anything you want to keep out of that
file before you press the button again. Explicit types, no `var`, same as
everywhere else in the package.

## The package never handles a route

The package plays no sound, spawns no VFX, launches no ragdoll and switches
no shader view from a route. A route is data — kind, id, note — and nothing
in this package ever acts on it. Making a route do something is the whole
point of the consumer stub: that's host code, and it's the only place a
route turns into a sound, a particle, a ragdoll launch or a shader swap.
