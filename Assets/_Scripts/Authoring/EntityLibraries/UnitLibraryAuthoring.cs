using Unity.Entities;
using UnityEngine;

public class UnitLibraryAuthoring : MonoBehaviour
{
    public UnitLibrarySO unitLibrary;
}

public class UnitLibraryBaker : Baker<UnitLibraryAuthoring>
{
    public override void Bake(UnitLibraryAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);

        AddComponent(entity, new UnitDataLibraryReference
        {
            library = authoring.unitLibrary
        });

        // Bake prefab entities into a buffer so the spawner can call Instantiate() at runtime.
        // Entities can't go in BlobAssets — this buffer is the DOTS-safe alternative.
        var prefabBuffer = AddBuffer<UnitPrefabEntry>(entity);
        if (authoring.unitLibrary != null && authoring.unitLibrary.units != null)
        {
            foreach (var unitSO in authoring.unitLibrary.units)
            {
                if (unitSO == null || unitSO.prefab == null) continue;
                prefabBuffer.Add(new UnitPrefabEntry
                {
                    unitType = unitSO.unitType,
                    prefab = GetEntity(unitSO.prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}