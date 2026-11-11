using Unity.Entities;
using UnityEngine;

public class AttackLibraryAuthoring : MonoBehaviour
{
    public AttackLibrarySO library;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<AttackLibraryAuthoring>
    {
        public override void Bake(AttackLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new AttackLibraryReference { library = authoring.library });
            AddComponent(entity, new AttackLibrary());
        }
    }
}
