using Unity.Entities;
using UnityEngine;

public class ZombieBrainAuthoring : MonoBehaviour
{
    public float awarenessRange;

    public class Baker : Baker<ZombieBrainAuthoring>
    {
        public override void Bake(ZombieBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            BrainBakeHelper.AddRequirements(this, entity, authoring.awarenessRange);
            AddComponent<PlayerOrder>(entity);

            DynamicBuffer<Behaviour> motivations = AddBuffer<Behaviour>(entity);
            BrainBakeHelper.AddBloodLust(ref motivations);
            BrainBakeHelper.AddSelfDefence(ref motivations);
        }
    }
}
