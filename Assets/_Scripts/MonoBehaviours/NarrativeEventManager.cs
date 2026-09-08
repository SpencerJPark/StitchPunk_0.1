using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Drives narrative event execution. Reads OnNarrativeEvent from the ECS singleton,
/// looks up the matching NarrativeEventSO, and runs its action groups via UniTask.
///
/// Groups execute sequentially; actions within a group run in parallel.
/// Uses destroyCancellationToken so all async work cancels cleanly on scene unload.
///
/// Setup:
///   1. Place ONE of these alongside a NarrativeEventAuthoring component.
///   2. Assign every NarrativeEventSO used in the scene to eventAssets.
///   3. NarrativeEventAuthoring bakes the singleton entity; this MonoBehaviour connects to it.
/// </summary>
public class NarrativeEventManager : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector references
    // ------------------------------------------------------------------

    [Header("Narrative Assets")]
    [Tooltip("Every NarrativeEventSO used in this scene. " +
             "A Dictionary keyed by eventId is built at Start for O(1) lookup.")]
    [SerializeField] private List<NarrativeEventSO> eventAssets;

    // ------------------------------------------------------------------
    // ECS references (resolved in Start)
    // ------------------------------------------------------------------

    private EntityManager _entityManager;
    private Entity _narrativeEntity;
    private Entity _dialogueManagerEntity;

    // ------------------------------------------------------------------
    // Asset and entity registries (built at Start)
    // ------------------------------------------------------------------

    private Dictionary<int, NarrativeEventSO> _eventRegistry;
    private Dictionary<int, Entity> _entityRegistry;

    // Trigger entities that should be re-armed after the current event.
    private readonly List<Entity> _repeatableTriggers = new List<Entity>();

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Start()
    {
        BuildEventRegistry();
    }

    // Resolved lazily from Update, never once from Start: in a SubScene project the baked entities
    // stream in a frame or more after Start runs, so a one-shot resolve there finds an empty world
    // and never retries. Same shape as DialogueUIManager.TryResolveEcsReferences, found there first —
    // silent on the ordinary "not streamed in yet" path; ReportUnresolvedEntitiesOnce below is the
    // only thing allowed to log, and only after a real grace period.
    private bool TryResolveEcsReferences()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        _entityManager = world.EntityManager;

        EntityQuery narrativeQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<NarrativeEventTag>());
        EntityQuery dialogueQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<DialogueManagerTag>());
        if (narrativeQuery.IsEmpty || dialogueQuery.IsEmpty) return false;

        _narrativeEntity = narrativeQuery.GetSingletonEntity();
        _dialogueManagerEntity = dialogueQuery.GetSingletonEntity();

        BuildEntityRegistry();
        return true;
    }

    private const float UnresolvedGraceSeconds = 5f;
    private float _unresolvedSeconds;
    private bool _hasReportedUnresolvedEntities;

    // A scene that genuinely has no NarrativeEventAuthoring/DialogueManagerAuthoring looks exactly
    // like one whose SubScene has not streamed in yet, so the complaint waits until it cannot be the
    // second — and is said once rather than every frame. Mirrors DialogueUIManager's own guard.
    private void ReportUnresolvedEntitiesOnce()
    {
        if (_hasReportedUnresolvedEntities) return;

        _unresolvedSeconds += Time.unscaledDeltaTime;
        if (_unresolvedSeconds < UnresolvedGraceSeconds) return;

        _hasReportedUnresolvedEntities = true;
        Debug.LogError(
            "NarrativeEventManager: still no NarrativeEventTag / DialogueManagerTag entity after "
            + UnresolvedGraceSeconds + "s. Narrative events will not run in this scene.");
    }

    private void Update()
    {
        if (_narrativeEntity == Entity.Null && !TryResolveEcsReferences())
        {
            ReportUnresolvedEntitiesOnce();
            return;
        }
        if (!_entityManager.IsComponentEnabled<OnNarrativeEvent>(_narrativeEntity)) return;

        // Read and immediately consume the signal so it fires exactly once.
        OnNarrativeEvent signal = _entityManager.GetComponentData<OnNarrativeEvent>(_narrativeEntity);
        _entityManager.SetComponentEnabled<OnNarrativeEvent>(_narrativeEntity, false);

        if (!_eventRegistry.TryGetValue(signal.eventId, out NarrativeEventSO narrativeSO))
        {
            Debug.LogWarning($"NarrativeEventManager: Event ID {signal.eventId} not registered. " +
                             "Add the NarrativeEventSO to the eventAssets list on NarrativeEventManager.");
            return;
        }

        _entityManager.SetComponentEnabled<ActiveNarrativeEvent>(_narrativeEntity, true);
        ExecuteEventAsync(narrativeSO, destroyCancellationToken).Forget();
    }

    // ------------------------------------------------------------------
    // Initialisation helpers
    // ------------------------------------------------------------------

    private void BuildEventRegistry()
    {
        _eventRegistry = new Dictionary<int, NarrativeEventSO>();
        foreach (NarrativeEventSO so in eventAssets)
        {
            if (so == null) continue;
            _eventRegistry[so.eventId] = so;
        }
    }

    private void BuildEntityRegistry()
    {
        _entityRegistry = new Dictionary<int, Entity>();

        if (_entityManager.Equals(default)) return;

        EntityQuery entityIdQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NarrativeEntityId>());

        NativeArray<Entity> entities = entityIdQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            NarrativeEntityId narrativeId = _entityManager.GetComponentData<NarrativeEntityId>(entity);
            _entityRegistry[narrativeId.id] = entity;
        }
        entities.Dispose();
        entityIdQuery.Dispose();
    }

    // ------------------------------------------------------------------
    // Event execution
    // ------------------------------------------------------------------

    private async UniTaskVoid ExecuteEventAsync(NarrativeEventSO so, CancellationToken ct)
    {
        CollectRepeatableTriggers(so.eventId);

        if (so.blockPlayerInput)
            _entityManager.SetComponentEnabled<CutsceneActiveTag>(_narrativeEntity, true);

        try
        {
            foreach (NarrativeActionGroup group in so.groups)
            {
                UniTask[] groupTasks = new UniTask[group.actions.Count];
                for (int actionIndex = 0; actionIndex < group.actions.Count; actionIndex++)
                    groupTasks[actionIndex] = ExecuteActionAsync(group.actions[actionIndex], ct);

                await UniTask.WhenAll(groupTasks);

                if (ct.IsCancellationRequested) break;
            }
        }
        finally
        {
            // Re-arm repeatable triggers so the event can fire again on the next approach.
            foreach (Entity triggerEntity in _repeatableTriggers)
                _entityManager.SetComponentEnabled<NarrativeTrigger>(triggerEntity, true);

            _entityManager.SetComponentEnabled<CutsceneActiveTag>(_narrativeEntity, false);
            _entityManager.SetComponentEnabled<ActiveNarrativeEvent>(_narrativeEntity, false);
        }
    }

    private void CollectRepeatableTriggers(int eventId)
    {
        _repeatableTriggers.Clear();

        EntityQuery triggerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NarrativeTrigger>());

        NativeArray<Entity> triggerEntities = triggerQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity triggerEntity in triggerEntities)
        {
            NarrativeTrigger trigger = _entityManager.GetComponentData<NarrativeTrigger>(triggerEntity);
            if (trigger.eventId == eventId && trigger.repeatable)
                _repeatableTriggers.Add(triggerEntity);
        }
        triggerEntities.Dispose();
        triggerQuery.Dispose();
    }

    private UniTask ExecuteActionAsync(NarrativeActionBase action, CancellationToken ct)
    {
        switch (action)
        {
            case DialogueTriggerAction dialogueAction:
                return ExecuteDialogueTriggerAsync(dialogueAction, ct);

            case MoveNPCAction moveAction:
                return ExecuteMoveNPCAsync(moveAction, ct);

            case PlayAnimationAction animAction:
                return ExecutePlayAnimationAsync(animAction, ct);

            case EnableComponentAction enableAction:
                ExecuteEnableComponent(enableAction);
                return UniTask.CompletedTask;

            case CinematicCameraAction cinCamAction:
                return ExecuteCinematicCameraAsync(cinCamAction, ct);

            case ZombifyAction zombifyAction:
                return ExecuteZombifyAsync(zombifyAction, ct);

            case PlayCutsceneAction playCutsceneAction:
                return ExecutePlayCutsceneAsync(playCutsceneAction, ct);

            default:
                Debug.LogWarning($"NarrativeEventManager: Unknown action type '{action.GetType().Name}'.");
                return UniTask.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // Per-action implementations
    // ------------------------------------------------------------------

    private async UniTask ExecuteDialogueTriggerAsync(DialogueTriggerAction action, CancellationToken ct)
    {
        if (_dialogueManagerEntity == Entity.Null) return;
        if (action.sequence == null)
        {
            Debug.LogWarning("NarrativeEventManager: DialogueTriggerAction has no sequence assigned.");
            return;
        }

        Entity speakerEntity = TryGetEntity(action.targetEntityId, out Entity speaker)
            ? speaker
            : Entity.Null;

        _entityManager.SetComponentData(_dialogueManagerEntity, new ActiveDialogue
        {
            sequenceId    = action.sequence.sequenceId,
            speakerEntity = speakerEntity,
        });
        _entityManager.SetComponentEnabled<ActiveDialogue>(_dialogueManagerEntity, true);

        await UniTask.WaitUntil(
            () => !_entityManager.IsComponentEnabled<ActiveDialogue>(_dialogueManagerEntity),
            cancellationToken: ct);
    }

    private async UniTask ExecuteMoveNPCAsync(MoveNPCAction action, CancellationToken ct)
    {
        if (!TryGetEntity(action.targetEntityId, out Entity npcEntity)) return;
        if (!TryGetEntity(action.waypointEntityId, out Entity waypointEntity)) return;

        if (!_entityManager.HasComponent<Movement>(npcEntity))
        {
            Debug.LogWarning($"NarrativeEventManager: Entity {npcEntity} has no Movement component.");
            return;
        }

        Unity.Mathematics.float3 destination =
            _entityManager.GetComponentData<LocalTransform>(waypointEntity).Position;

        Movement movement = _entityManager.GetComponentData<Movement>(npcEntity);
        movement.targetPosition = destination;
        movement.isMoving       = true;
        _entityManager.SetComponentData(npcEntity, movement);

        await UniTask.WaitUntil(
            () => !_entityManager.GetComponentData<Movement>(npcEntity).isMoving,
            cancellationToken: ct);
    }

    private async UniTask ExecutePlayAnimationAsync(PlayAnimationAction action, CancellationToken ct)
    {
        if (!TryGetEntity(action.targetEntityId, out Entity entity)) return;

        if (action.animationKey == 0)
        {
            Debug.LogWarning("NarrativeEventManager: PlayAnimationAction has no animationKey assigned.");
            return;
        }

        if (!_entityManager.HasBuffer<AnimationCommand>(entity)
            || !_entityManager.HasComponent<AnimationCommandPending>(entity))
        {
            Debug.LogWarning($"NarrativeEventManager: Entity {entity} has no AnimationCommand buffer.");
            return;
        }

        // PlaybackApi.PlayAnimation itself needs an EnabledRefRW from a ComponentLookup, which is
        // internal to EntityManager outside a job/system — so this builds the same command shape
        // (CommandKind.PlayAnimation, keyed) by hand, same as before the cutover.
        _entityManager.GetBuffer<AnimationCommand>(entity).Add(new AnimationCommand
        {
            kind          = CommandKind.PlayAnimation,
            layerIndex    = 0,
            clip          = default,
            speed         = 1f,
            loop          = action.looping ? LoopMode.Loop : LoopMode.Once,
            blendDuration = float.NaN,
            animationKey  = action.animationKey,
        });
        _entityManager.SetComponentEnabled<AnimationCommandPending>(entity, true);

        if (action.waitForCompletion)
        {
            await UniTask.WaitUntil(() => !IsAnimationPlaying(entity, action.animationKey),
                cancellationToken: ct);
        }
        else if (action.duration > 0f)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(action.duration),
                cancellationToken: ct);
        }
    }

    private bool IsAnimationPlaying(Entity entity, uint animationKey)
    {
        if (!_entityManager.Exists(entity)) return false;
        if (!_entityManager.HasBuffer<PlaybackLayer>(entity)) return false;

        DynamicBuffer<PlaybackLayer> layers = _entityManager.GetBuffer<PlaybackLayer>(entity);
        return PlaybackApi.IsAnimationPlaying(layers, animationKey);
    }

    private void ExecuteEnableComponent(EnableComponentAction action)
    {
        if (!TryGetEntity(action.targetEntityId, out Entity entity)) return;

        switch (action.componentType)
        {
            case NarrativeToggleType.DialogueProvider:
                if (_entityManager.HasComponent<DialogueProvider>(entity))
                    _entityManager.SetComponentEnabled<DialogueProvider>(entity, action.enable);
                break;

            case NarrativeToggleType.InteractionProvider:
                if (_entityManager.HasComponent<Interaction>(entity))
                    _entityManager.SetComponentEnabled<Interaction>(entity, action.enable);
                break;
        }
    }

    private async UniTask ExecuteZombifyAsync(ZombifyAction action, CancellationToken ct)
    {
        if (!TryGetEntity(action.targetEntityId, out Entity entity)) return;

        if (!_entityManager.HasComponent<ZombifyRequest>(entity))
        {
            Debug.LogWarning($"NarrativeEventManager: Entity {entity} has no ZombifyRequest — only " +
                             "units baked through UnitBakingUtil can be converted.");
            return;
        }

        _entityManager.SetComponentData(entity, new ZombifyRequest
        {
            targetUnitType = action.targetUnitType,
            delaySeconds   = action.delaySeconds,
        });
        _entityManager.SetComponentEnabled<ZombifyRequest>(entity, true);

        // ZombifySystem disables the request once it has composed the conversion — including the
        // case where the unit has no zombie form, so this never waits forever.
        if (action.waitForConversion)
        {
            await UniTask.WaitUntil(
                () => !_entityManager.Exists(entity)
                      || !_entityManager.IsComponentEnabled<ZombifyRequest>(entity),
                cancellationToken: ct);
        }
    }

    private async UniTask ExecutePlayCutsceneAsync(PlayCutsceneAction action, CancellationToken ct)
    {
        if (action.cutscene == null)
        {
            Debug.LogWarning("NarrativeEventManager: PlayCutsceneAction has no cutscene assigned.");
            return;
        }

        Entity signalEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(signalEntity, new CutsceneRequest
        {
            cutsceneKey = action.cutscene.StableId,
            speed       = action.speed,
        });

        if (action.overrides != null && action.overrides.Count > 0)
        {
            DynamicBuffer<CutsceneRequestBindingOverride> overridesBuffer =
                _entityManager.AddBuffer<CutsceneRequestBindingOverride>(signalEntity);

            for (int overrideIndex = 0; overrideIndex < action.overrides.Count; overrideIndex++)
            {
                CutsceneSlotEntityOverride slotOverride = action.overrides[overrideIndex];
                if (!TryGetEntity(slotOverride.narrativeEntityId, out Entity targetEntity))
                    continue;

                overridesBuffer.Add(new CutsceneRequestBindingOverride
                {
                    slotId = slotOverride.slotId,
                    target = targetEntity,
                });
            }
        }

        if (!action.waitForCompletion) return;

        // CutsceneStartSystem consumes the signal on a later ECS tick — wait for the cutscene to
        // actually become active (or be dropped, e.g. a stage lookup miss) before waiting for it
        // to end. Checking "!ActiveCutscene enabled" alone would resolve immediately, since the
        // request has not started yet the instant this signal is created.
        await UniTask.WaitUntil(
            () => _entityManager.IsComponentEnabled<ActiveCutscene>(_narrativeEntity)
                || !_entityManager.Exists(signalEntity),
            cancellationToken: ct);

        if (_entityManager.IsComponentEnabled<ActiveCutscene>(_narrativeEntity))
        {
            await UniTask.WaitUntil(
                () => !_entityManager.IsComponentEnabled<ActiveCutscene>(_narrativeEntity),
                cancellationToken: ct);
        }
    }

    private async UniTask ExecuteCinematicCameraAsync(CinematicCameraAction action, CancellationToken ct)
    {
        Vector3 shotPosition = Vector3.zero;

        if (action.targetEntityId != -1 && TryGetEntity(action.targetEntityId, out Entity targetEntity))
        {
            Unity.Mathematics.float3 entityPos =
                _entityManager.GetComponentData<LocalTransform>(targetEntity).Position;
            shotPosition = new Vector3(entityPos.x, entityPos.y, entityPos.z) + action.targetOffset;
        }

        CameraManager.Instance.EnterCinematic(shotPosition);

        if (action.holdDuration > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(action.holdDuration), cancellationToken: ct);

        CameraManager.Instance.ExitCinematic();
    }

    // ------------------------------------------------------------------
    // Entity lookup helper
    // ------------------------------------------------------------------

    private bool TryGetEntity(int narrativeEntityId, out Entity entity)
    {
        if (narrativeEntityId != -1 && _entityRegistry.TryGetValue(narrativeEntityId, out entity))
            return true;

        Debug.LogWarning($"NarrativeEventManager: No entity registered with NarrativeEntityId {narrativeEntityId}. " +
                         "Add NarrativeEntityIdAuthoring with the matching ID to the target GameObject.");
        entity = Entity.Null;
        return false;
    }
}
