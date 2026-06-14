# Cross-Entity Baking — Full Pattern

A `Baker<T>` may only write components to the entity returned by `GetEntity()` for its **own** GameObject. When you need to distribute components to child entities (e.g., per-joint ragdoll state, per-quad animation targets), split the work in two.

## Step 1 — authoring stores refs on the root entity only

```csharp
using Unity.Entities;
using UnityEngine;

public class FooRootAuthoring : MonoBehaviour
{
    public GameObject visualChild;
    public GameObject[] joints;

    public class Baker : Baker<FooRootAuthoring>
    {
        public override void Bake(FooRootAuthoring authoring)
        {
            Entity rootEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(rootEntity, new FooRootConfig
            {
                visualChild = GetEntity(authoring.visualChild, TransformUsageFlags.Dynamic),
            });

            DynamicBuffer<FooJointRef> jointRefs = AddBuffer<FooJointRef>(rootEntity);
            if (authoring.joints != null)
            {
                for (int jointIndex = 0; jointIndex < authoring.joints.Length; jointIndex++)
                {
                    Entity jointEntity = GetEntity(authoring.joints[jointIndex], TransformUsageFlags.Dynamic);
                    jointRefs.Add(new FooJointRef { joint = jointEntity });
                }
            }
        }
    }
}
```

Key points:
- `GetEntity(authoring.someGameObject, ...)` creates or retrieves an entity reference for the given GO. This is how the baker records child entity refs without adding components to them.
- The root's buffer/component is the handoff — the baking system reads it.

## Step 2 — `PostBakingSystemGroup` system distributes the components

```csharp
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial class FooBakingSystem : SystemBase
{
    protected override void OnUpdate()
    {
        EntityManager entityManager = EntityManager;

        NativeList<Entity> jointEntitiesToTag = new NativeList<Entity>(Allocator.Temp);

        foreach ((RefRO<FooRootConfig> config, DynamicBuffer<FooJointRef> jointRefs, Entity rootEntity)
                 in SystemAPI.Query<RefRO<FooRootConfig>, DynamicBuffer<FooJointRef>>().WithEntityAccess())
        {
            Entity visualChild = config.ValueRO.visualChild;
            if (!entityManager.HasComponent<FooVisualState>(visualChild))
                jointEntitiesToTag.Add(visualChild);

            for (int jointIndex = 0; jointIndex < jointRefs.Length; jointIndex++)
                jointEntitiesToTag.Add(jointRefs[jointIndex].joint);
        }

        // Apply adds AFTER the query — never inside it (structural change throws)
        for (int listIndex = 0; listIndex < jointEntitiesToTag.Length; listIndex++)
        {
            Entity targetEntity = jointEntitiesToTag[listIndex];
            entityManager.AddComponentData(targetEntity, new FooJointState());
            entityManager.SetComponentEnabled<FooJointState>(targetEntity, false);
        }

        jointEntitiesToTag.Dispose();
    }
}
```

Key points:
- `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` is mandatory — without it the system runs at runtime, not during bake, and breaks.
- `[UpdateInGroup(typeof(PostBakingSystemGroup))]` places it after all `Baker<T>` classes have run.
- `SystemBase` (not `ISystem`) because baking systems need managed `EntityManager` access for structural changes.
- **Collect entities in a `NativeList` during the query, apply the `AddComponentData` calls AFTER the loop.** Calling `AddComponentData` inside the `foreach` throws `InvalidOperationException: Structural changes are not allowed while iterating over entities`.
- Always `Dispose()` the list.

## Reference implementation

The canonical example in the project is the ragdoll setup:

- `Assets/_Scripts/Authoring/Units/Ragdoll2DRootAuthoring.cs`
- `Assets/_Scripts/Systems/PostBakingSystemGroup/Ragdoll2DBakingSystem.cs`

Read both side-by-side before writing a new cross-entity baker.

## When you don't need this pattern

If every component you're adding goes onto the baker's own GameObject's entity, you don't need a baking system at all — everything fits in `Bake()`. Only reach for this pattern when the entities receiving components are DIFFERENT from the one you're baking. Adding it preemptively is overengineering.
