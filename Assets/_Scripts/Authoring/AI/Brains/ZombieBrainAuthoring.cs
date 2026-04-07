using Unity.Entities;
using UnityEngine;

public class ZombieBrainAuthoring : MonoBehaviour
{
    public GameObject body;
    public float awarenessRange;

    public class Baker : Baker<ZombieBrainAuthoring>
    {
        public override void Bake(ZombieBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<ZombieBrain>(entity);

            BrainBakeHelper.AddRequirements(this, entity, authoring.body, authoring.awarenessRange);
            BrainBakeHelper.AddPlayerControllable(this, entity);

            AddComponent<PlayerOrder>(entity);
        }
    }
}
