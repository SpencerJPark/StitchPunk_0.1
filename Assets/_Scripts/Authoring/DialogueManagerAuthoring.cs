using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place ONE of these GameObjects in every scene that uses dialogue.
/// Bakes the DialogueManager singleton entity — holds ActiveDialogue,
/// OnDialogueEvent, and DialogueManagerTag.
/// </summary>
public class DialogueManagerAuthoring : MonoBehaviour
{
    public class Baker : Baker<DialogueManagerAuthoring>
    {
        public override void Bake(DialogueManagerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new DialogueManagerTag());

            AddComponent(entity, new ActiveDialogue());
            SetComponentEnabled<ActiveDialogue>(entity, false);

            AddComponent(entity, new OnDialogueEvent());
            SetComponentEnabled<OnDialogueEvent>(entity, false);
        }
    }
}
