using Unity.Entities;

// One-frame signal entity, never baked: spawned by PlayCutsceneAction or CutsceneDebugTrigger, destroyed by CutsceneStartSystem the same frame.
public struct CutsceneRequest : IComponentData
{
    // CutsceneAsset.StableId of the stage to play.
    public ulong cutsceneKey;

    // 1 is normal.
    public float speed;
}

// Wins over the stage's baked CutsceneStageBinding for the same slot; for actors spawned at runtime.
public struct CutsceneRequestBindingOverride : IBufferElementData
{
    public uint slotId;
    public Entity target;
}

// Baked disabled on units and the player; CutsceneStartSystem enables it on every bound entity. The single gate that suppresses AI, movement input and facing.
public struct CutsceneActor : IComponentData, IEnableableComponent { }

// Edge detector for the toolkit's CutsceneMoveToMark: one order becomes exactly one BeginPathRequest and one HaltPathing.
// Reset by SpawnStateInitSystem. Never set on the Player, who walks to the mark by hand.
public struct CutsceneMarkIssued : IComponentData, IEnableableComponent { }

// On the NarrativeEventTag singleton, baked disabled. Only one cutscene runs at a time; a second request while enabled is dropped with a warning.
public struct ActiveCutscene : IComponentData, IEnableableComponent
{
    public Entity playRequest;
}
