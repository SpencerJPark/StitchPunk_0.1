using Unity.Entities;
using UnityEngine;

public class CurrentInteractionAuthoring : MonoBehaviour
{
    public class Baker : Baker<CurrentInteractionAuthoring>
    {
        public override void Bake(CurrentInteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CurrentInteraction>(entity);
        }
    }
}

public struct CurrentInteraction : IComponentData
{
    public Entity target;
    public ActionType action;
    public AnimationType animation;
    public float timeRemaining;
    public float interactionRange;
    public NeedModifiers needModifiers;
    public bool isInRange;
}