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

        AddComponent(entity, new UnitLibraryReference
        {
            library = authoring.unitLibrary
        });
    }
}