using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Pack alarm: a unit that has spotted a target (RaiseAlarm) enables AllyAlert on same-faction
// neighbours in radius, and forces an immediate re-think. The response job then emits combat
// options at ALERT_RESPONSE_PRIORITY (3, same tier as EnemyAwareness/SelfDefence; the GuardPost
// leash at 5 still strips it outside the leash). Deliberately NOT gated on ActionRequest.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
[UpdateBefore(typeof(GuardPostAwarenessSystem))]
public partial struct AllyAlertSystem : ISystem
{
    public const float ALERT_COOLDOWN_SECONDS = 5f;
    public const float ALERT_DURATION_SECONDS = 6f;
    public const int   ALERT_RESPONSE_PRIORITY = 3;

    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<AllyAlert>      _allyAlertLookup;
    private ComponentLookup<ThinkRate>      _thinkRateLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactionRegistry>();
        state.RequireForUpdate<UnitDataLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        _deadLookup      = state.GetComponentLookup<Dead>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _allyAlertLookup = state.GetComponentLookup<AllyAlert>(false);
        _thinkRateLookup = state.GetComponentLookup<ThinkRate>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _deadLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _allyAlertLookup.Update(ref state);
        _thinkRateLookup.Update(ref state);

        FactionRegistry factionRegistry = SystemAPI.GetSingleton<FactionRegistry>();
        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new RaiseAlarmBroadcastJob
        {
            factionEntities  = factionRegistry.entities,
            transformLookup  = _transformLookup,
            allyAlertLookup  = _allyAlertLookup,
            thinkRateLookup  = _thinkRateLookup,
            deadLookup       = _deadLookup,
            deltaTime        = deltaTime,
        }.Schedule(state.Dependency);

        state.Dependency = new AllyAlertResponseJob
        {
            deadLookup      = _deadLookup,
            transformLookup = _transformLookup,
            unitLibrary     = unitLibrary,
            aiConfig        = brainLibrary.blob,
            deltaTime       = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct RaiseAlarmBroadcastJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity> factionEntities;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    public ComponentLookup<AllyAlert>                          allyAlertLookup;
    public ComponentLookup<ThinkRate>                          thinkRateLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    public float deltaTime;

    public void Execute(
        Entity                            self,
        ref RaiseAlarm                    raiseAlarm,
        in Faction                        faction,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        in DynamicBuffer<UtilityActions>  actions)
    {
        raiseAlarm.cooldownRemaining = math.max(0f, raiseAlarm.cooldownRemaining - deltaTime);
        if (raiseAlarm.cooldownRemaining > 0f) return;

        Entity target = Entity.Null;
        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            UtilityActions option = actions[actionIndex];
            if (option.actionType.IsCombatAction()
                && option.actionType != ActionType.Flee
                && option.targetEntity != Entity.Null)
            {
                target = option.targetEntity;
                break;
            }
        }

        if (target == Entity.Null
            && stateMachine.action.IsCombatAction()
            && stateMachine.action != ActionType.Flee
            && stateMachine.targetEntity != Entity.Null)
        {
            target = stateMachine.targetEntity;
        }

        if (target == Entity.Null) return;
        if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target)) return;

        float radiusSq = raiseAlarm.radius * raiseAlarm.radius;

        if (factionEntities.TryGetFirstValue((byte)faction.factionType, out Entity ally,
                out NativeParallelMultiHashMapIterator<byte> iterator))
        {
            do
            {
                if (ally == self) continue;
                if (!allyAlertLookup.HasComponent(ally)) continue;
                if (deadLookup.HasComponent(ally) && deadLookup.IsComponentEnabled(ally)) continue;
                if (!transformLookup.TryGetComponent(ally, out LocalTransform allyTransform)) continue;
                if (math.distancesq(transform.Position, allyTransform.Position) > radiusSq) continue;

                bool alreadyAlertedToTarget = allyAlertLookup.IsComponentEnabled(ally)
                    && allyAlertLookup[ally].target == target;

                allyAlertLookup[ally] = new AllyAlert
                {
                    target    = target,
                    remaining = AllyAlertSystem.ALERT_DURATION_SECONDS,
                };

                if (alreadyAlertedToTarget) continue;

                allyAlertLookup.SetComponentEnabled(ally, true);
                if (thinkRateLookup.HasComponent(ally))
                {
                    ThinkRate thinkRate = thinkRateLookup[ally];
                    thinkRate.timeUntilNextThink = 0f;
                    thinkRateLookup[ally] = thinkRate;
                }
            }
            while (factionEntities.TryGetNextValue(out ally, ref iterator));
        }

        raiseAlarm.cooldownRemaining = AllyAlertSystem.ALERT_COOLDOWN_SECONDS;
    }

}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(AllyAlert))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct AllyAlertResponseJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Dead>                deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>      transformLookup;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>  unitLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    public float deltaTime;

    public void Execute(
        in UtilityBrain                   brain,
        in StateMachine                   stateMachine,
        ref AllyAlert                     allyAlert,
        EnabledRefRW<AllyAlert>           allyAlertEnabled,
        ref DynamicBuffer<Motivation>     motivations,
        ref DynamicBuffer<UtilityActions> actions)
    {
        allyAlert.remaining -= deltaTime;

        bool targetGone = allyAlert.target == Entity.Null
            || !transformLookup.HasComponent(allyAlert.target)
            || (deadLookup.HasComponent(allyAlert.target) && deadLookup.IsComponentEnabled(allyAlert.target));

        if (allyAlert.remaining <= 0f || targetGone)
        {
            allyAlertEnabled.ValueRW = false;
            return;
        }

        // No-reactivation guard: already fighting (or fleeing) — same as SelfDefence.
        if (stateMachine.action.IsCombatAction()) return;

        int unitIndex = unitLibrary.Value.FindByUnitType(brain.unitType);
        if (unitIndex < 0) return;
        ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];

        AIUtils.SetMotivationValue(ref motivations, NeedType.BloodLust, 100f);
        for (int attackIndex = 0; attackIndex < unitBlob.attacks.Length; attackIndex++)
        {
            ActionType actionType = unitBlob.attacks[attackIndex].action;
            int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, actionType);
            if (defIndex < 0) continue;

            actions.Add(new UtilityActions
            {
                actionType       = actionType,
                actionDefIndex   = defIndex,
                priority         = AllyAlertSystem.ALERT_RESPONSE_PRIORITY,
                needsValidation  = false,
                targetEntity     = allyAlert.target,
            });
        }
    }
}
