using Unity.Collections;

// Burst-compatible enum → name helpers for log strings. Burst's FixedString interpolation
// cannot stringify enums (no reflection) — `{someEnum}` prints the TYPE name ("BehaviorType"),
// not the value. Returning string literals from a switch converts at compile time, so these
// are legal inside [BurstCompile] jobs. Enums are append-only: add a case when adding a value;
// the default prints the numeric value so an unmapped entry degrades visibly, never silently.
public static class EnumLogNames
{
    public static FixedString32Bytes Name(this ActionType value)
    {
        switch (value)
        {
            case ActionType.Idle:                 return "Idle";
            case ActionType.Wander:               return "Wander";
            case ActionType.Interact:             return "Interact";
            case ActionType.Death:                return "Death";
            case ActionType.Resurrection:         return "Resurrection";
            case ActionType.Flee:                 return "Flee";
            case ActionType.Repair:               return "Repair";
            case ActionType.Build:                return "Build";
            case ActionType.Eat:                  return "Eat";
            case ActionType.Sleep:                return "Sleep";
            case ActionType.Talk:                 return "Talk";
            case ActionType.Smoke:                return "Smoke";
            case ActionType.Drink:                return "Drink";
            case ActionType.Patrol:               return "Patrol";
            case ActionType.SeekEntertainment:    return "SeekEntertainment";
            case ActionType.Bathroom:             return "Bathroom";
            case ActionType.Sit:                  return "Sit";
            case ActionType.MeleeContinuous:      return "MeleeContinuous";
            case ActionType.MeleeSingle:          return "MeleeSingle";
            case ActionType.ProjectileContinuous: return "ProjectileContinuous";
            case ActionType.ProjectileSingle:     return "ProjectileSingle";
            case ActionType.Swing:                return "Swing";
            case ActionType.Throw:                return "Throw";
            case ActionType.Shoot:                return "Shoot";
            case ActionType.Spawn:                return "Spawn";
            case ActionType.EquipWeapon:          return "EquipWeapon";
            case ActionType.UseHealingItem:       return "UseHealingItem";
            default:
            {
                FixedString32Bytes fallback = default;
                fallback.Append((int)value);
                return fallback;
            }
        }
    }

    public static FixedString32Bytes Name(this BehaviorType value)
    {
        switch (value)
        {
            case BehaviorType.None:            return "None";
            case BehaviorType.Wander:          return "Wander";
            case BehaviorType.MeleeSwing:      return "MeleeSwing";
            case BehaviorType.Flee:            return "Flee";
            case BehaviorType.Sit:             return "Sit";
            case BehaviorType.Pickup:          return "Pickup";
            case BehaviorType.Talk:            return "Talk";
            case BehaviorType.MeleeContinuous: return "MeleeContinuous";
            case BehaviorType.MeleeSingle:     return "MeleeSingle";
            default:
            {
                FixedString32Bytes fallback = default;
                fallback.Append((int)value);
                return fallback;
            }
        }
    }

    public static FixedString32Bytes Name(this BehaviorCommandType value)
    {
        switch (value)
        {
            case BehaviorCommandType.PlayAnimation:         return "PlayAnimation";
            case BehaviorCommandType.SpawnEntity:           return "SpawnEntity";
            case BehaviorCommandType.ModifyStat:            return "ModifyStat";
            case BehaviorCommandType.StartDialogue:         return "StartDialogue";
            case BehaviorCommandType.ApplyForce:            return "ApplyForce";
            case BehaviorCommandType.WaitTime:              return "WaitTime";
            case BehaviorCommandType.Approach:              return "Approach";
            case BehaviorCommandType.RequestAttack:         return "RequestAttack";
            case BehaviorCommandType.RequestPickup:         return "RequestPickup";
            case BehaviorCommandType.ModifyMotivation:      return "ModifyMotivation";
            case BehaviorCommandType.FleeFromTarget:        return "FleeFromTarget";
            case BehaviorCommandType.ReleaseInteraction:    return "ReleaseInteraction";
            case BehaviorCommandType.LoopUntil:             return "LoopUntil";
            case BehaviorCommandType.StopAnimation:         return "StopAnimation";
            case BehaviorCommandType.PlayActionAnimation:   return "PlayActionAnimation";
            case BehaviorCommandType.RequestSocialResponse: return "RequestSocialResponse";
            default:
            {
                FixedString32Bytes fallback = default;
                fallback.Append((int)value);
                return fallback;
            }
        }
    }
}
