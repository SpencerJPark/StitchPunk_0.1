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

            // Read prefab GameObjects directly from the UnitSO entries so no extra
            // Inspector field is needed — the SO is the single source of truth.
            // Body and brain are baked as separate prefab entities so the spawner can
            // instantiate them independently (fixes IEnableableComponent bit copying
            // and enables runtime brain-swapping).
            var maleCitizenSO = authoring.unitLibrary != null
                ? authoring.unitLibrary.GetUnitSO(UnitType.MaleCitizen)
                : null;

            AddComponent(entity, new UnitPrefabEntry
            {
                maleCitizenPrefab = maleCitizenSO?.prefab != null
                    ? GetEntity(maleCitizenSO.prefab, TransformUsageFlags.Dynamic)
                    : Entity.Null,
                maleCitizenBrainPrefab = maleCitizenSO?.brainPrefab != null
                    ? GetEntity(maleCitizenSO.brainPrefab, TransformUsageFlags.Dynamic)
                    : Entity.Null,
            });
        }
    }
}