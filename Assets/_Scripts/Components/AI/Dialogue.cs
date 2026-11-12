using Unity.Entities;

public struct DialogueProvider : IComponentData, IEnableableComponent
{
    // Maps to a constant in DialogueIds.Sequences.
    public int sequenceId;
}

public struct DialogueManagerTag : IComponentData { }

// Enabled while a sequence plays; ECS systems check it to block conflicting interactions.
public struct ActiveDialogue : IComponentData, IEnableableComponent
{
    // Maps to a constant in DialogueIds.Sequences.
    public int sequenceId;

    public Entity speakerEntity;
}

// DialogueEventSystem re-broadcasts it each frame it is enabled, then disables it after one frame.
public struct OnDialogueEvent : IComponentData, IEnableableComponent
{
    // Maps to a constant in DialogueIds.Events.
    public int eventId;
}

// Persistent: serialized via the save system.
public struct PlayedDialogue : IBufferElementData
{
    public int sequenceId;
}
