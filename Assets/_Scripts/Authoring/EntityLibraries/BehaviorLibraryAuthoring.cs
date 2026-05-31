using Unity.Entities;
using UnityEngine;

public class BehaviorLibraryAuthoring : MonoBehaviour
{
    public BehaviorLibrarySO library;

    public class Baker : Baker<BehaviorLibraryAuthoring>
    {
        public override void Bake(BehaviorLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            // Re-bake when any behavior's command sequence changes.
            foreach (BehaviorSO behavior in authoring.library.behaviors)
            {
                if (behavior != null) DependsOn(behavior);
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BehaviorLibraryReference { library = authoring.library });
            AddComponent(entity, new BehaviorLibrary());
        }
    }
}
