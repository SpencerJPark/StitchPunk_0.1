using Unity.Entities;

// ---------------------------------------------------------------------------
// One-frame signal entity (baked by nothing — spawned by NarrativeEventManager's
// PlayCutsceneAction or CutsceneDebugTrigger). CutsceneStartSystem consumes and
// destroys it the same frame (the LoggingSystem lifecycle: create, read once, destroy).
// ---------------------------------------------------------------------------

/// <summary>
/// Requests that a baked <c>DotsAnimationToolkit.CutsceneStage</c> start playing. Consumed and
/// destroyed by <c>CutsceneStartSystem</c> the same frame.
/// </summary>
public struct CutsceneRequest : IComponentData
{
    /// <summary>The <c>CutsceneAsset.StableId</c> of the stage to play — how <c>CutscenePlaybackApi.TryFindStage</c> finds it.</summary>
    public ulong cutsceneKey;

    /// <summary>Which playback layer clip blocks target on every bound actor.</summary>
    public byte layerIndex;

    /// <summary>Initial playback speed; 1 is normal.</summary>
    public float speed;
}

/// <summary>
/// One binding override on a <see cref="CutsceneRequest"/> entity — wins over the stage's own
/// baked <c>CutsceneStageBinding</c> for the same slot. For actors spawned at runtime rather than
/// staged in the subscene.
/// </summary>
public struct CutsceneRequestBindingOverride : IBufferElementData
{
    public uint slotId;
    public Entity target;
}

/// <summary>
/// Baked disabled on every unit (see <c>UnitBakingUtil.BakeRequirements</c>) and on the player.
/// Enabled by <c>CutsceneStartSystem</c> on every entity a running cutscene binds — the single gate
/// that suppresses AI decision-making, movement input, and facing while a cutscene puppets an actor.
/// </summary>
public struct CutsceneActor : IComponentData, IEnableableComponent { }

/// <summary>
/// Enabled while a game-side path request is out for the toolkit's <c>CutsceneMoveToMark</c> order.
/// It is the edge detector: the mark's own enabled bit says "walk there", this says "we already
/// asked", so one order becomes exactly one <c>BeginPathRequest</c> and one <c>HaltPathing</c>.
/// Baked disabled on every unit (<c>UnitBakingUtil.BakeRequirements</c>), reset by
/// <c>SpawnStateInitSystem</c>. Never set on the <c>Player</c>, who walks to their mark by hand.
/// </summary>
public struct CutsceneMarkIssued : IComponentData, IEnableableComponent { }

/// <summary>
/// Baked disabled on the <c>NarrativeEventTag</c> singleton (see <c>NarrativeEventAuthoring</c>).
/// Enabled by <c>CutsceneStartSystem</c> while a cutscene is running; <c>playRequest</c> is the
/// toolkit's own request entity (carries <c>CutscenePlaybackState</c>). Only one cutscene may run
/// at a time — a second request while this is enabled is dropped with a warning.
/// </summary>
public struct ActiveCutscene : IComponentData, IEnableableComponent
{
    public Entity playRequest;
}
