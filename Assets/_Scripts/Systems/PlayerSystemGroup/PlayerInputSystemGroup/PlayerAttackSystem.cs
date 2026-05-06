using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// 1. Create an Aspect to handle the 8+ parameters
public readonly partial struct PlayerAttackAspect : IAspect
{
    public readonly Entity Self;
    public readonly RefRO<LocalTransform> Transform;
    public readonly RefRO<PlayerSelectedAttack> SelectedAttack;
    public readonly RefRW<AttackRequest> AttackRequest;
    public readonly EnabledRefRW<AttackRequest> AttackRequestEnabled;
    public readonly EnabledRefRW<OnAttackPlayerInput> AttackInputEnabled;
    public readonly RefRO<PlayerActionMap> ActionMap;
    public readonly RefRW<ActionTimer> ActionTimer;
    public readonly EnabledRefRW<ActionTimer> ActionTimerEnabled;
}

[UpdateInGroup(typeof(PlayerInputSystemGroup))]
[BurstCompile]
public partial struct PlayerAttackSystem : ISystem
{
    private const float RANGED_CONE_HALF_ANGLE_DEG = 45f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibrary>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<AnimationLibrary>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;
        var unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        var animationLibrary = SystemAPI.GetSingleton<AnimationLibrary>().library;
        float cosHalfAngle = math.cos(math.radians(RANGED_CONE_HALF_ANGLE_DEG));

        // Use the Aspect in the query to bypass the 7-parameter limit
        foreach (var p in SystemAPI.Query<PlayerAttackAspect>()
                     .WithAll<Player>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (p.ActionMap.ValueRO.activeActionMap != ActionMaps.Player) continue;
            if (!p.AttackInputEnabled.ValueRO) continue;

            p.AttackInputEnabled.ValueRW = false; // Consume input

            // GATE: Stop if the action timer is still running
            if (p.ActionTimerEnabled.ValueRO && p.ActionTimer.ValueRO.time > 0f) continue;

            // Fetch UnitData (which isn't in our aspect)
            if (!SystemAPI.HasComponent<UnitData>(p.Self)) continue;
            UnitData unitData = SystemAPI.GetComponent<UnitData>(p.Self);
            
            int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
            if (unitIndex < 0 || unitIndex >= unitLibrary.Value.units.Length) continue;

            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
            if (unitBlob.attacks.Length == 0) continue;

            AttackType attackType = p.SelectedAttack.ValueRO.attackType != AttackType.None
                ? p.SelectedAttack.ValueRO.attackType
                : unitBlob.attacks[0].attack;
            
            ActionType actionType = AIUtils.GetActionByAttack(ref unitBlob, attackType);
            int attackIndex = (int)attackType;
            if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
            ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

            float3 playerPos = p.Transform.ValueRO.Position;
            float3 facingDir = math.normalizesafe(new float3(math.forward(p.Transform.ValueRO.Rotation).x, 0, math.forward(p.Transform.ValueRO.Rotation).z));

            bool isMelee = actionType == ActionType.MeleeContinuous || actionType == ActionType.MeleeSingle;
            float rangeSq = attackBlob.range * attackBlob.range;

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float bestDot = float.MinValue;

            // Target Detection
            foreach (var (enemyTransform, enemyEntity) in SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<Health, Alive>().WithNone<Player, PlayerImmune>().WithEntityAccess())
            {
                float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
                toEnemy.y = 0f;
                float distSq = math.lengthsq(toEnemy);
                if (distSq < 0.0001f || distSq > rangeSq) continue;

                if (isMelee)
                {
                    if (distSq < bestDistSq) { bestDistSq = distSq; bestTarget = enemyEntity; }
                }
                else
                {
                    float dot = math.dot(facingDir, toEnemy * math.rsqrt(distSq));
                    if (dot < cosHalfAngle) continue;
                    if (dot > bestDot) { bestDot = dot; bestTarget = enemyEntity; }
                }
            }

            if (bestTarget == Entity.Null) continue;

            // Fire the Attack
            p.AttackRequest.ValueRW.targetEntity = bestTarget;
            p.AttackRequest.ValueRW.attackType = attackType;
            p.AttackRequest.ValueRW.hitFired = false;
            p.AttackRequestEnabled.ValueRW = true;

            // Handle Animation & Timer
            AnimationType animType = GetAttackAnimation(ref unitBlob, actionType);
            float animDuration = animationLibrary.Value.clips[(int)animType].duration;
            
            // Safety: Ensure duration is at least one frame to prevent infinite fire
            p.ActionTimer.ValueRW.time = math.max(animDuration, 0.05f);
            p.ActionTimerEnabled.ValueRW = true;

            var setAnimations = SystemAPI.GetBuffer<SetAnimation>(p.Self);
            setAnimations.Add(new SetAnimation
            {
                layer = AnimationLayerType.Action,
                animation = animType,
                speed = 1f,
                looping = false,
            });
            SystemAPI.SetComponentEnabled<AnimationRequest>(p.Self, true);
        }
    }

    private static AnimationType GetAttackAnimation(ref UnitDataBlob unitBlob, ActionType actionType)
    {
        ref var mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == actionType) return mappings[i].animation;
        }
        return AnimationType.None;
    }
}

// 2. THE TIMER TICK SYSTEM (Add this to your project!)
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ActionTimerTickSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        foreach (var (timer, enabled) in SystemAPI.Query<RefRW<ActionTimer>, EnabledRefRW<ActionTimer>>())
        {
            if (timer.ValueRO.time > 0f)
            {
                timer.ValueRW.time -= dt;
            }
            else
            {
                enabled.ValueRW = false; // Shut off timer once it hits 0
            }
        }
    }
}