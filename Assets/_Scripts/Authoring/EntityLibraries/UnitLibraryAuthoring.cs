using Unity.Entities;
using UnityEngine;

public class UnitLibraryAuthoring : MonoBehaviour
{
    public UnitLibrarySO unitLibrary;

    public class UnitLibraryBaker : Baker<UnitLibraryAuthoring>
    {
        public override void Bake(UnitLibraryAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new UnitDataLibraryReference
            {
                library = authoring.unitLibrary
            });

            // Each UnitSO in the library becomes one DynamicBuffer element.
            // The SO is the single source of truth — no extra Inspector fields needed here.
            // Body and brain are baked as separate prefab entities so the spawner can
            // instantiate them independently (fixes IEnableableComponent bit copying
            // and enables runtime brain-swapping).
            var buffer = AddBuffer<UnitPrefabEntry>(entity);
            if (authoring.unitLibrary == null) return;

            foreach (UnitSO unitSO in authoring.unitLibrary.units)
            {
                if (unitSO == null) continue;
                buffer.Add(new UnitPrefabEntry
                {
                    unitType   = unitSO.unitType,
                    bodyPrefab  = unitSO.prefab != null
                        ? GetEntity(unitSO.prefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    brainPrefab = unitSO.brainPrefab != null
                        ? GetEntity(unitSO.brainPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                });
            }
        }
    }
}