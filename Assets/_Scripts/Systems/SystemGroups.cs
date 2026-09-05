using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;

// ============================================================================================
// SystemGroups.cs — the single manifest for every StitchPunk ComponentSystemGroup.
// Rules (enforced by SystemPlacementConformanceTests in Assets/_Scripts/Tests/):
//   1. Every group is declared in THIS file — never inline next to a system.
//   2. A system file lives in the folder named after the group it updates in.
//   3. Top-level feature groups derive from GameSceneSystemGroup so the GameSceneTag gate
//      lives in ONE place — child systems only declare their own DATA requirements
//      (RequireForUpdate<SomeLibrary> etc.).
// ============================================================================================

// Base for every top-level gameplay feature group: the whole feature updates only when a
// GameSceneTag entity exists (baked via the GameSceneTag prefab in the active subscene).
// When the requirement is unmet the group skips ALL child systems — this is the single
// plug-point for scene gating, and later for per-feature toggle singletons.
public abstract partial class GameSceneSystemGroup : ComponentSystemGroup
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireForUpdate<GameSceneTag>();
    }
}

// --------------------------------------------------------------------------------------------
// World services — NOT scene-gated. Charter: frame-setup infrastructure only — registries,
// spatial hashes, event buses (DamageBusSystem), floating origin. Runs OrderFirst so every
// feature can rely on its singletons being reset/current. Features may READ its singletons;
// it never reads feature state. Anything that is gameplay logic does not belong here.
// --------------------------------------------------------------------------------------------
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(PlayerSystemGroup))]
public partial class GameManagerSystemGroup : ComponentSystemGroup { }

// --------------------------------------------------------------------------------------------
// SimulationSystemGroup pipeline:
// GameManager → Player → Cutscene → UtilityAI → MinionActionSelection → StateMachine → Item
//   → Movement → Buildings → Combat → Health → Design → Animation
// (Adjacent edges below are asserted by SystemGroupOrderTests — keep them explicit.)
// --------------------------------------------------------------------------------------------

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(UtilityAISystemGroup))]
public partial class PlayerSystemGroup : GameSceneSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup), OrderFirst = true)]
        public partial class PlayerInputSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup))]
        [UpdateAfter(typeof(PlayerInputSystemGroup))]
        [UpdateBefore(typeof(DialogueSystemGroup))]
        public partial class NarrativeSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup))]
        [UpdateAfter(typeof(NarrativeSystemGroup))]
        [UpdateBefore(typeof(PlayerEquipmentSystemGroup))]
        public partial class DialogueSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(PlayerSystemGroup), OrderLast = true)]
        public partial class PlayerEquipmentSystemGroup : ComponentSystemGroup { }

// Sits between Player and UtilityAI: a cutscene that starts this frame must gate AI selection
// this same frame, and a request spawned by NarrativeEventManager (a MonoBehaviour Update, i.e.
// before SimulationSystemGroup) is consumed the same frame it is created.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSystemGroup))]
[UpdateBefore(typeof(UtilityAISystemGroup))]
public partial class CutsceneSystemGroup : GameSceneSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(StateMachineSystemGroup))]
public partial class UtilityAISystemGroup : GameSceneSystemGroup { }

        [UpdateInGroup(typeof(UtilityAISystemGroup))]
        [UpdateBefore(typeof(UtilityAwarenessSystemGroup))]
        public partial class UtilityMotivationSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(UtilityAISystemGroup))]
        public partial class UtilityAwarenessSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(UtilityAISystemGroup))]
[UpdateBefore(typeof(StateMachineSystemGroup))]
public partial class MinionActionSelectionSystemGroup : GameSceneSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ItemSystemGroup))]
public partial class StateMachineSystemGroup : GameSceneSystemGroup { }

        [UpdateInGroup(typeof(StateMachineSystemGroup))]
        [UpdateBefore(typeof(ActionExecutionSystemGroup))]
        public partial class ActionSelectionSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(StateMachineSystemGroup))]
        public partial class ActionExecutionSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystemGroup))]
public partial class ItemSystemGroup : GameSceneSystemGroup { }

        [UpdateInGroup(typeof(ItemSystemGroup), OrderFirst = true)]
        public partial class ItemEquipSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(ItemSystemGroup))]
        [UpdateAfter(typeof(ItemEquipSystemGroup))]
        public partial class ThrownItemSystemGroup : ComponentSystemGroup { }

// MovementSystemGroup itself (and its four sub-groups) now lives in the DOTS Movement
// Toolkit package (DotsMovementToolkit.MovementSystemGroup) — see the `using` above. A
// package group can't derive from this file's GameSceneSystemGroup or carry attributes
// declared from game code, so the game-relative edges below live on ITEM/BUILDINGS instead.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(CombatSystemGroup))]
public partial class BuildingsSystemGroup : GameSceneSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(HealthSystemGroup))]
public partial class CombatSystemGroup : GameSceneSystemGroup { }

        [UpdateInGroup(typeof(CombatSystemGroup))]
        [UpdateBefore(typeof(CombatReactionSystemGroup))]
        public partial class CombatExecutionSystemGroup : ComponentSystemGroup { }

        [UpdateInGroup(typeof(CombatSystemGroup))]
        [UpdateAfter(typeof(CombatExecutionSystemGroup))]
        public partial class CombatReactionSystemGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AnimationSystemGroup))]
public partial class HealthSystemGroup : GameSceneSystemGroup { }

// Runs after health/revive (so a conversion can re-skin) and before animation (so the image-index
// push in AnimationExecutionSystemGroup picks up the change the same frame). Home of DesignChangeSystem.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(AnimationSystemGroup))]
public partial class DesignSystemGroup : GameSceneSystemGroup { }

// AnimationToolkitSystemGroup lives in the DOTS Animation Toolkit package (no ordering edges of
// its own — see DotsAnimationToolkit.AnimationToolkitSystemGroup's remarks). The game orders
// against it here so commands this group issues this frame apply this frame.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AnimationToolkitSystemGroup))]
public partial class AnimationSystemGroup : GameSceneSystemGroup { }

        // OrderFirst was load-bearing against AnimationExecutionSystemGroup (OrderLast), which is
        // gone with the legacy read-side systems — kept as documentation that this group is meant
        // to run before whatever else ends up in AnimationSystemGroup.
        [UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
        public partial class AnimationAssignmentSystemGroup : ComponentSystemGroup { }

// --------------------------------------------------------------------------------------------
// LateSimulationSystemGroup pipeline: Spawn → SpawnInit → Sound → Despawn → Save
// Ragdoll is no longer a game-side LateSimulation group — the toolkit's AnimationToolkitRagdollSystemGroup
// runs earlier, inside SimulationSystemGroup's AnimationToolkitPresentationSystemGroup (see
// AnimationToolkitSystemGroups.cs), ordered after BillboardResolveSystem and before SocketResolveSystem.
// --------------------------------------------------------------------------------------------

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(SpawnInitSystemGroup))]
public partial class SpawnSystemGroup : GameSceneSystemGroup { }

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateBefore(typeof(DespawnSystemGroup))]
public partial class SpawnInitSystemGroup : GameSceneSystemGroup { }

// Gathers/culls requested sounds late (after all gameplay + spawn-init has emitted them) and writes
// the ResolvedVoices + WorldMood + MusicState singletons the AudioManager reads each LateUpdate.
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[UpdateAfter(typeof(SpawnInitSystemGroup))]
[UpdateBefore(typeof(DespawnSystemGroup))]
public partial class SoundSystemGroup : GameSceneSystemGroup { }

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class DespawnSystemGroup : GameSceneSystemGroup { }

// Runs last in LateSimulationSystemGroup — all spawns, despawns, and game logic are settled.
[UpdateInGroup(typeof(LateSimulationSystemGroup), OrderLast = true)]
public partial class SaveSystemGroup : GameSceneSystemGroup { }
