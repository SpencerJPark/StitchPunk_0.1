using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class BrainLibraryAuthoring : MonoBehaviour
{
    public BrainLibrarySO library;

    public class Baker : Baker<BrainLibraryAuthoring>
    {
        public override void Bake(BrainLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<BrainLibraryEntry> brainBuffer = AddBuffer<BrainLibraryEntry>(entity);

            foreach (BrainSO brain in authoring.library.brains)
            {
                if (brain == null) continue;

                // Depend on the brain and its actions so inline-curve edits re-trigger baking.
                DependsOn(brain);
                if (brain.actions != null)
                {
                    foreach (UtilityActionSO action in brain.actions)
                    {
                        if (action != null) DependsOn(action);
                    }
                }

                brainBuffer.Add(new BrainLibraryEntry { config = brain });
            }

            AddComponent(entity, new BrainLibrary());
        }
    }
}
