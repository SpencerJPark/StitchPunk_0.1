using Unity.Entities;

// ---------------------------------------------------------------------------
// NPC entity components
// ---------------------------------------------------------------------------

/// <summary>
/// Marks an NPC entity as having dialogue the player can trigger.
/// Baked by DialogueProviderAuthoring.
/// </summary>
public struct DialogueProvider : IComponentData, IEnableableComponent
{
    /// <summary>Primary sequence ID. Maps to a constant in DialogueIds.Sequences.</summary>
    public int sequenceId;
}

// ---------------------------------------------------------------------------
// DialogueManager singleton entity components (baked by DialogueManagerAuthoring)
// ---------------------------------------------------------------------------

/// <summary>Singleton marker. Use SystemAPI.GetSingletonEntity&lt;DialogueManagerTag&gt;() to find the entity.</summary>
public struct DialogueManagerTag : IComponentData { }

/// <summary>
/// Enabled when a dialogue sequence is actively playing.
/// ECS systems check this to block conflicting interactions during dialogue.
/// UIManager (MonoBehaviour) reads sequenceId to look up the SO and drives line progression.
/// </summary>
public struct ActiveDialogue : IComponentData, IEnableableComponent
{
    /// <summary>Which sequence is playing. Maps to a constant in DialogueIds.Sequences.</summary>
    public int sequenceId;

    /// <summary>The NPC entity that triggered this dialogue.</summary>
    public Entity speakerEntity;
}

/// <summary>
/// Enabled by DialogueUIManager when a dialogue choice fires a game event.
/// DialogueEventSystem (ECS) re-broadcasts this each frame it is enabled so that
/// downstream systems (e.g. TournamentUnlockSystem) can react.
/// Disabled by DialogueEventSystem after one frame.
/// </summary>
public struct OnDialogueEvent : IComponentData, IEnableableComponent
{
    /// <summary>Event ID. Maps to a constant in DialogueIds.Events.</summary>
    public int eventId;
}

// ---------------------------------------------------------------------------
// GameData entity buffers (persistent — serialized via save system)
// ---------------------------------------------------------------------------

/// <summary>Tracks which primary dialogue sequences have been played at least once.</summary>
public struct PlayedDialogue : IBufferElementData
{
    public int sequenceId;
}
