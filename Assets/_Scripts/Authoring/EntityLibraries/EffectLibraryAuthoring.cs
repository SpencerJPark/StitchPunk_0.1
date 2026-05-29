using Unity.Entities;
using UnityEngine;

public class EffectLibraryAuthoring : MonoBehaviour
{
    public EffectLibrarySO library;

    public class Baker : Baker<EffectLibraryAuthoring>
    {
        public override void Bake(EffectLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EffectLibraryReference { library = authoring.library });
            AddComponent(entity, new EffectLibrary());
        }
    }
}
