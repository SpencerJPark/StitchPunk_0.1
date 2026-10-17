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

                DependOnBrainGraph(brain);
                brainBuffer.Add(new BrainLibraryEntry { config = brain });
            }

            AddComponent(entity, new BrainLibrary());
        }

        // Depends on every brain in the base chain plus their actions and modules, so inline-curve
        // edits anywhere in the inheritance graph re-trigger baking. Instance method: DependsOn is one.
        private void DependOnBrainGraph(BrainSO libraryBrain)
        {
            HashSet<BrainSO> visitedBrains = new HashSet<BrainSO>();
            BrainSO currentBrain = libraryBrain;

            while (currentBrain != null && visitedBrains.Add(currentBrain))
            {
                DependsOn(currentBrain);

                if (currentBrain.actions != null)
                {
                    foreach (UtilityActionSO action in currentBrain.actions)
                    {
                        if (action != null) DependsOn(action);
                    }
                }

                if (currentBrain.modules != null)
                {
                    foreach (BehaviourModuleSO module in currentBrain.modules)
                    {
                        if (module == null) continue;

                        DependsOn(module);
                        if (module.action != null) DependsOn(module.action);
                    }
                }

                currentBrain = currentBrain.baseBrain;
            }
        }
    }
}
