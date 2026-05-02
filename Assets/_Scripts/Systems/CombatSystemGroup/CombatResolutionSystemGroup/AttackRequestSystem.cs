using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(CombatResolutionSystemGroup))]
public partial struct AttackRequestSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private BufferLookup<Hurt>             hurtBufferLookup;
    private ComponentLookup<Alive>         aliveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<AttackLibrary>();

        transformLookup  = state.GetComponentLookup<LocalTransform>(true);
        hurtBufferLookup = state.GetBufferLookup<Hurt>(false);
        aliveLookup      = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        hurtBufferLookup.Update(ref state);
        aliveLookup.Update(ref state);

        BlobAssetReference<AttackLibraryBlob> attackLibrary =
            SystemAPI.GetSingleton<AttackLibrary>().library;

        // Single-threaded: multiple attackers may write to the same target's Hurt buffer
        state.Dependency = new AttackRequestJob
        {
            transformLookup  = transformLookup,
            hurtBufferLookup = hurtBufferLookup,
            aliveLookup      = aliveLookup,
            attackLibrary    = attackLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
public partial struct AttackRequestJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    public            BufferLookup<Hurt>              hurtBufferLookup;
    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;
    [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;

    public void Execute(
        Entity                           attackerEntity,
        in LocalTransform                attackerTransform,
        ref AttackRequest                attackRequest,
        in DynamicBuffer<AnimationLayer> layers,
        EnabledRefRW<AttackRequest>      attackRequestEnabled)
    {
        // Find the Action animation layer
        float actionLayerTime   = -1f;
        bool  actionLayerActive = false;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].layer == AnimationLayerType.Action)
            {
                actionLayerTime   = layers[i].time;
                actionLayerActive = layers[i].active;
                break;
            }
        }

        // Action layer not yet present — wait one frame for animation assignment
        if (actionLayerTime < 0f)
            return;

        int attackIndex = (int)attackRequest.attackType;
        if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
            return;
        ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];

        // Hit-frame check: fire damage the first frame animation time crosses hitTime.
        // Using >= means this fires correctly even when a large deltaTime skips past hitTime.
        if (!attackRequest.hitFired && actionLayerTime >= attackBlob.hitTime)
        {
            Entity victim = attackRequest.targetEntity;

            bool victimAlive = aliveLookup.HasComponent(victim) &&
                               aliveLookup.IsComponentEnabled(victim);

            if (victimAlive &&
                transformLookup.TryGetComponent(victim, out LocalTransform victimTransform) &&
                hurtBufferLookup.HasBuffer(victim))
            {
                float distanceSq = math.distancesq(attackerTransform.Position, victimTransform.Position);

                if (distanceSq <= attackBlob.range * attackBlob.range)
                {
                    hurtBufferLookup[victim].Add(new Hurt
                    {
                        attackerEntity = attackerEntity,
                        attackType     = attackBlob.attackType,
                        distance       = math.sqrt(distanceSq),
                        damageAmount   = attackBlob.damageAmount,
                        hitSourceX     = attackerTransform.Position.x,
                        ragdollForce   = attackBlob.ragdollForce,
                        launchForceY   = attackBlob.launchForceY,
                        launchForceX   = attackBlob.launchForceX,
                    });
                }
            }

            // Mark fired regardless — prevents re-attempts if target stepped out of range
            attackRequest.hitFired       = true;
            attackRequestEnabled.ValueRW = false;
            return;
        }

        // Fallback: animation finished without ever firing a hit (target left range).
        // Disarm so the action system can re-queue once cooldown expires.
        if (!actionLayerActive)
        {
            attackRequestEnabled.ValueRW = false;
            attackRequest.hitFired       = false;
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(CombatResolutionSystemGroup))]
public partial struct AttackCooldownSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new AttackCooldownJob { deltaTime = deltaTime }
            .ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AttackCooldownJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref AttackCooldown cooldown)
    {
        if (cooldown.timer > 0f)
            cooldown.timer = math.max(0f, cooldown.timer - deltaTime);
    }
}
