using Unity.Entities;
using UnityEngine;

public class LifetimeAuthoring : MonoBehaviour
{
    public float seconds = 3f;

    public class Baker : Baker<LifetimeAuthoring>
    {
        public override void Bake(LifetimeAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Lifetime { secondsRemaining = authoring.seconds });

            AddComponent(entity, new Despawn { mode = DespawnMode.Auto });
            SetComponentEnabled<Despawn>(entity, false);
        }
    }
}
