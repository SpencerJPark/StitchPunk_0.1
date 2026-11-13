using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

public struct UnitStateLookups
{
    [ReadOnly] public ComponentLookup<LocalTransform> transform;
    [ReadOnly] public ComponentLookup<Dead> dead;

    public static UnitStateLookups Create(ref SystemState state)
    {
        return new UnitStateLookups
        {
            transform = state.GetComponentLookup<LocalTransform>(true),
            dead = state.GetComponentLookup<Dead>(true),
        };
    }

    public void Update(ref SystemState state)
    {
        transform.Update(ref state);
        dead.Update(ref state);
    }

    public readonly bool IsDead(Entity unit) => EntityUtils.IsDead(in dead, unit);

    public readonly bool IsAlive(Entity unit) => EntityUtils.IsAlive(in dead, unit);

    // Missing Dead component counts as not dead (matches `if (IsDead) skip; TryGetComponent`).
    public readonly bool TryGetTransformUnlessDead(Entity unit, out LocalTransform unitTransform)
    {
        if (EntityUtils.IsDead(in dead, unit))
        {
            unitTransform = default;
            return false;
        }
        return transform.TryGetComponent(unit, out unitTransform);
    }

    // Requires Dead present and disabled (matches `IsAlive && TryGetComponent`).
    public readonly bool TryGetTransformIfAlive(Entity unit, out LocalTransform unitTransform)
    {
        if (!EntityUtils.IsAlive(in dead, unit))
        {
            unitTransform = default;
            return false;
        }
        return transform.TryGetComponent(unit, out unitTransform);
    }
}
