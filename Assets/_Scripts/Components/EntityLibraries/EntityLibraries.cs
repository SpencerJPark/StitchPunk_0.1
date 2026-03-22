using Unity.Entities;
using UnityEngine;

public struct ScoringLibrary : IComponentData
{
    public BlobAssetReference<AIScoringLibraryBlob> library;
}
public struct ScoringLibraryReference : IComponentData
{
    public UnityObjectRef<AIScoringLibrarySO> library;
}


public struct AnimationLibrary : IComponentData {
    public BlobAssetReference<AnimationLibraryBlob> library;
}
public struct AnimationLibraryReference : IComponentData
{
    public UnityObjectRef<AnimationLibrarySO> library;
}


public struct UnitDataLibrary : IComponentData
{
    public BlobAssetReference<UnitLibraryBlob> library;
}

public struct UnitDataLibraryReference : IComponentData
{
    public UnityObjectRef<UnitLibrarySO> library;
}


public struct AttackLibrary : IComponentData
{
    public BlobAssetReference<AttackLibraryBlob> library;
}

public struct AttackLibraryReference : IComponentData
{
    public UnityObjectRef<AttackLibrarySO> library;
}


// Entities cannot be stored in BlobAssets, so prefab entity references live in a DynamicBuffer.
// Baked by UnitLibraryBaker; consumed by UnitSpawnerSystem at runtime.
public struct UnitPrefabEntry : IBufferElementData
{
    public UnitType unitType;
    public Entity prefab;
}