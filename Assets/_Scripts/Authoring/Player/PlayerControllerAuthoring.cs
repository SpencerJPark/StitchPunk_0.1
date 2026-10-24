using DotsMovementToolkit;
using Unity.Entities;
using UnityEngine;

public class PlayerControllerAuthoring : MonoBehaviour
{
    [Tooltip("Drag the empty hand socket child (EquipSocketAuthoring) here.")]
    public GameObject handSocket;

    [Tooltip("Child GameObject that acts as the aim arrow visual (shown while aiming).")]
    public GameObject aimIndicator;

    [Header("Combat")]
    [SearchableEnum] public DamageSource defaultAttack = DamageSource.Slash;

    [Header("Roll")]
    [Tooltip("World units covered by one roll.")]
    public float rollDistance = 3.2f;
    [Tooltip("Seconds from start to landing.")]
    public float rollDuration = 0.34f;
    [Tooltip("How sharply the roll slows down. 0 = constant speed, higher = faster start and longer glide out.")]
    public float rollSpeedCurvePower = 1.6f;
    [Tooltip("Invulnerable from the start of the roll until this fraction of it (0-1).")]
    [Range(0f, 1f)] public float rollInvulnerableUntilFraction = 0.8f;
    [Tooltip("Seconds after landing before the next roll can start.")]
    public float rollCooldown = 0.2f;
    [Tooltip("A roll pressed this many seconds before it is allowed still comes out.")]
    public float rollInputBufferWindow = 0.15f;
    [Tooltip("An attack pressed in the last this-many seconds of a roll fires on landing.")]
    public float rollAttackBufferWindow = 0.15f;
    [Range(0, 8)] public int rollAfterimageCount = 3;
    public float rollAfterimageLifetime = 0.18f;
    public Color rollAfterimageTint = new Color(0.6f, 0.95f, 0.9f, 0.55f);
    [Tooltip("Full turns the visual spins during one roll.")]
    public float rollSpinTurns = 1f;
    [Tooltip("Height above the feet that the visual spins around (about the middle of the body).")]
    public float rollVisualPivotHeight = 0.9f;
    public float rollLandingSquashDuration = 0.12f;
    [Tooltip("Hostile units closer than this (centre to centre) push the walking player out. Rolling ignores it.")]
    public float bodyBlockRadius = 1f;
    [Tooltip("Fastest the push-out moves the player. Keep it above walk speed or enemies stop blocking.")]
    public float bodyBlockPushSpeed = 8f;

    [Header("Debug — Starting Equipment")]
    [SearchableEnum] public ItemType debugSlot1 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot2 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot3 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot4 = ItemType.None;

    [Header("Minion Groups")]
    [Tooltip("Number of group slots available at start (1–4). Upgrade system will write this at runtime.")]
    [Range(1, 4)] public int startingGroupCount = 2;

    [Tooltip("On foot, quick commands only reach squad members within this distance (CampLoop CL-D7).")]
    public float onFootCommandRadius = 15f;

    [Tooltip("One scene-object marker per group slot (index 0 = group 1). Shown at the order destination, hidden on arrival or overwrite.")]
    public GameObject[] groupOrderMarkers = new GameObject[4];

    [Header("Resources")]
    public int startingWood = 10;
    public int startingScrapMetal = 5;
    public int startingCorpses = 2;
    public int startingElectricity = 5;
    public int startingCurrency = 0;
    public int startingRevivalRounds = 3;

    [Header("Workstation")]
    [Tooltip("Prefab with WorkstationAuthoring, instantiated when the player deploys (CampLoop_System.md §5).")]
    public GameObject workstationPrefab;

    public class Baker : Baker<PlayerControllerAuthoring> {

        public override void Bake(PlayerControllerAuthoring controllerAuthoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player());

            AddComponent(entity, new PlayerActionMap{ activeActionMap = ActionMaps.Player});
            
            AddComponent(entity, new Faction
            {
                factionType = FactionType.Player,
            });

            // Reviver targeting: PlayerTargetingSystem writes the nearest PlayerInteractable
            // (e.g. a revivable corpse) here; PlayerReviverSystem reads it. Both query
            // WithPresent<Target>, so the component must exist on the player — baked disabled
            // (no target until one is in range). The player is not a UnitAuthoring entity, so
            // this is the only place Target gets added to the player.

            AddComponent(entity, new MovePlayerInput());
            SetComponentEnabled<MovePlayerInput>(entity, false);

            AddComponent(entity, new LookPlayerInput());
            SetComponentEnabled<LookPlayerInput>(entity, false);

            AddComponent(entity, new CursorPlayerInput());
            SetComponentEnabled<CursorPlayerInput>(entity, false);

            AddComponent(entity, new ZoomPlayerInput());
            SetComponentEnabled<ZoomPlayerInput>(entity, false);

            AddComponent(entity, new PlayerSelectedAttack { damageSource = controllerAuthoring.defaultAttack });

            // Player melee combat scaffolding — maintained/consumed by the PlayerInputSystemGroup
            // combat systems. Both start disabled: no target and off cooldown.
            AddComponent(entity, new CombatTarget());
            SetComponentEnabled<CombatTarget>(entity, false);

            AddComponent(entity, new AttackCooldown());
            SetComponentEnabled<AttackCooldown>(entity, false);

            AddComponent(entity, new OnAttackPlayerInput());
            SetComponentEnabled<OnAttackPlayerInput>(entity, false);

            AddComponent(entity, new OnInteractPlayerInput());
            SetComponentEnabled<OnInteractPlayerInput>(entity, false);

            AddComponent(entity, new OnRollPlayerInput());
            SetComponentEnabled<OnRollPlayerInput>(entity, false);

            AddComponent(entity, new PlayerRollSettings
            {
                rollDistance = controllerAuthoring.rollDistance,
                rollDuration = Mathf.Max(0.05f, controllerAuthoring.rollDuration),
                speedCurvePower = Mathf.Max(0f, controllerAuthoring.rollSpeedCurvePower),
                invulnerableUntilFraction = controllerAuthoring.rollInvulnerableUntilFraction,
                cooldownAfterRoll = controllerAuthoring.rollCooldown,
                inputBufferWindow = controllerAuthoring.rollInputBufferWindow,
                attackBufferWindow = controllerAuthoring.rollAttackBufferWindow,
                afterimageCount = controllerAuthoring.rollAfterimageCount,
                afterimageLifetime = controllerAuthoring.rollAfterimageLifetime,
                afterimageTint = new Unity.Mathematics.float4(
                    controllerAuthoring.rollAfterimageTint.r,
                    controllerAuthoring.rollAfterimageTint.g,
                    controllerAuthoring.rollAfterimageTint.b,
                    controllerAuthoring.rollAfterimageTint.a),
                spinTurns = controllerAuthoring.rollSpinTurns,
                visualPivotHeight = controllerAuthoring.rollVisualPivotHeight,
                landingSquashDuration = controllerAuthoring.rollLandingSquashDuration,
                bodyBlockRadius = controllerAuthoring.bodyBlockRadius,
                bodyBlockPushSpeed = controllerAuthoring.bodyBlockPushSpeed,
            });

            AddComponent(entity, new PlayerRoll());
            SetComponentEnabled<PlayerRoll>(entity, false);

            AddComponent(entity, new RollCooldown());
            SetComponentEnabled<RollCooldown>(entity, false);

            AddComponent(entity, new DodgeInvulnerable());
            SetComponentEnabled<DodgeInvulnerable>(entity, false);

            AddComponent(entity, new OnSneakPlayerInput());
            SetComponentEnabled<OnSneakPlayerInput>(entity, false);

            AddComponent(entity, new OnEquipmentSlotPlayerInput());
            SetComponentEnabled<OnEquipmentSlotPlayerInput>(entity, false);
            AddComponent(entity, new PlayerEquipmentSlots
            {
                itemSlot1 = controllerAuthoring.debugSlot1,
                itemSlot2 = controllerAuthoring.debugSlot2,
                itemSlot3 = controllerAuthoring.debugSlot3,
                itemSlot4 = controllerAuthoring.debugSlot4,
            });

            AddComponent(entity, new OnDropPlayerInput());
            SetComponentEnabled<OnDropPlayerInput>(entity, false);

            AddComponent(entity, new AimPlayerInput());
            SetComponentEnabled<AimPlayerInput>(entity, false);

            AddComponent(entity, new AimDirection { direction = new Unity.Mathematics.float3(0f, 0f, 1f) });

            Entity indicatorEntity = controllerAuthoring.aimIndicator != null
                ? GetEntity(controllerAuthoring.aimIndicator, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new AimIndicatorRef { visualEntity = indicatorEntity });

            AddComponent(entity, new OnMinionMoveCommand());
            SetComponentEnabled<OnMinionMoveCommand>(entity, false);

            AddComponent(entity, new OnMinionInteractCommand());
            SetComponentEnabled<OnMinionInteractCommand>(entity, false);

            AddComponent(entity, new OnMinionAttackCommand());
            SetComponentEnabled<OnMinionAttackCommand>(entity, false);

            AddComponent(entity, new OnMinionDefendCommand());
            SetComponentEnabled<OnMinionDefendCommand>(entity, false);

            AddComponent(entity, new OnMinionFollowCommand());
            SetComponentEnabled<OnMinionFollowCommand>(entity, false);

            // Minion group input components
            AddComponent(entity, new OnCycleGroupInput());
            SetComponentEnabled<OnCycleGroupInput>(entity, false);

            AddComponent(entity, new OnQuickCommandInput());
            SetComponentEnabled<OnQuickCommandInput>(entity, false);

            AddComponent(entity, new OnSelectPlayerInput());
            SetComponentEnabled<OnSelectPlayerInput>(entity, false);

            AddComponent(entity, new OnCommandPlayerInput());
            SetComponentEnabled<OnCommandPlayerInput>(entity, false);

            AddComponent(entity, new OnSnapCameraBackInput());
            SetComponentEnabled<OnSnapCameraBackInput>(entity, false);

            // Minion group data — one group unlocked by default
            AddComponent(entity, new PlayerMinionGroupsData
            {
                unlockedGroupCount = controllerAuthoring.startingGroupCount,
                assignmentGroupIndex = 0
            });

            AddComponent(entity, new PlayerSquadSettings { onFootCommandRadius = controllerAuthoring.onFootCommandRadius });

            // Create 4 persistent horde entities (one per group slot) and store refs in buffer
            DynamicBuffer<PlayerHordeSlot> hordeSlots = AddBuffer<PlayerHordeSlot>(entity);
            for (int slotIndex = 0; slotIndex < 4; slotIndex++)
            {
                // Named so the Entities window doesn't show these as "Player" duplicates.
                Entity hordeEntity = CreateAdditionalEntity(
                    TransformUsageFlags.None, false, $"Player Horde {slotIndex + 1}");

                Entity markerEntity = Entity.Null;
                if (controllerAuthoring.groupOrderMarkers != null
                    && slotIndex < controllerAuthoring.groupOrderMarkers.Length
                    && controllerAuthoring.groupOrderMarkers[slotIndex] != null)
                {
                    markerEntity = GetEntity(controllerAuthoring.groupOrderMarkers[slotIndex], TransformUsageFlags.Dynamic);
                }

                AddComponent(hordeEntity, new Horde
                {
                    hordeId        = slotIndex + 1,
                    targetPosition = new Unity.Mathematics.float3(0f, 0f, 0f),
                    targetEntity   = Entity.Null,
                    flowFieldIndex = -1,
                    memberCount    = 0,
                    isActive       = true,
                    needsPathUpdate = false,
                });
                AddComponent(hordeEntity, new HordeOrderMarker { markerEntity = markerEntity });
                AddBuffer<HordeMemberBuffer>(hordeEntity);
                hordeSlots.Add(new PlayerHordeSlot { hordeEntity = hordeEntity });
            }

            AddComponent(entity, new SelectionBoxData());
            SetComponentEnabled<SelectionBoxData>(entity, false);

            AddComponent(entity, new CursorScreenPosition());

            Entity socketEntity = controllerAuthoring.handSocket != null
                ? GetEntity(controllerAuthoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquip { socketEntity = socketEntity });

            // One row per ResourceType in enum order so the ledger and snapshot agree on a fixed layout.
            DynamicBuffer<ResourceStack> resourceStacks = AddBuffer<ResourceStack>(entity);
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.Wood, amount = controllerAuthoring.startingWood });
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.ScrapMetal, amount = controllerAuthoring.startingScrapMetal });
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.Corpse, amount = controllerAuthoring.startingCorpses });
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.Electricity, amount = controllerAuthoring.startingElectricity });
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.Currency, amount = controllerAuthoring.startingCurrency });
            resourceStacks.Add(new ResourceStack { resourceType = ResourceType.RevivalRound, amount = controllerAuthoring.startingRevivalRounds });
            AddBuffer<StorageCapacity>(entity);
            AddBuffer<ResourceChangeRequest>(entity);
            AddComponent(entity, new ResourceShortfall());
            SetComponentEnabled<ResourceShortfall>(entity, false);
            AddComponent(entity, new PlayerResourcesSnapshot
            {
                wood          = controllerAuthoring.startingWood,
                scrapMetal    = controllerAuthoring.startingScrapMetal,
                corpse        = controllerAuthoring.startingCorpses,
                electricity   = controllerAuthoring.startingElectricity,
                currency      = controllerAuthoring.startingCurrency,
                revivalRound  = controllerAuthoring.startingRevivalRounds,
            });

            // The player is not baked through UnitBakingUtil — add the cutscene gate here.
            // Workstation deploy/mount state — all baked off; WorkstationInputSystemGroup drives them.
            Entity workstationPrefabEntity = controllerAuthoring.workstationPrefab != null
                ? GetEntity(controllerAuthoring.workstationPrefab, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new WorkstationPrefabReference { prefab = workstationPrefabEntity });
            AddComponent(entity, new PlayerWorkstationLink { workstation = Entity.Null });
            AddComponent(entity, new OnDeployWorkstationInput());
            SetComponentEnabled<OnDeployWorkstationInput>(entity, false);
            AddComponent(entity, new PlayerWorkstationPlacement());
            SetComponentEnabled<PlayerWorkstationPlacement>(entity, false);
            AddComponent(entity, new WorkstationMounted());
            SetComponentEnabled<WorkstationMounted>(entity, false);
            AddComponent(entity, new WorkstationDismountRequest());
            SetComponentEnabled<WorkstationDismountRequest>(entity, false);

            // Health and Dead come from HealthAuthoring on the BaseUnit parent prefab (100 HP there);
            // adding them here is a duplicate-component bake error. PlayerKnockoutSystem clears Dead.
            // Threat bookkeeping only registers sources whose faction is in the victim's AttackFaction list.
            AddBuffer<ThreatEntry>(entity);
            DynamicBuffer<AttackFaction> attackFactions = AddBuffer<AttackFaction>(entity);
            attackFactions.Add(new AttackFaction { faction = FactionType.Bandits });
            attackFactions.Add(new AttackFaction { faction = FactionType.Rottens });

            AddComponent(entity, new PlayerKnockedOut());
            SetComponentEnabled<PlayerKnockedOut>(entity, false);

            AddComponent(entity, new CutsceneActor());
            SetComponentEnabled<CutsceneActor>(entity, false);
        }
    }
}

