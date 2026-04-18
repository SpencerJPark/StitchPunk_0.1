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

public struct UnitPrefabEntry : IBufferElementData
{
    public UnitType unitType;
    public Entity bodyPrefab;
}


public struct AttackLibrary : IComponentData
{
    public BlobAssetReference<AttackLibraryBlob> library;
}

public struct AttackLibraryReference : IComponentData
{
    public UnityObjectRef<AttackLibrarySO> library;
}


public struct FactoryLibrary : IComponentData
{
    public BlobAssetReference<FactoryLibraryBlob> blob;
}

public struct FactoryLibraryReference : IComponentData
{
    public UnityObjectRef<FactoryLibrarySO> library;
}


public struct BrainLibrary : IComponentData
{
    public BlobAssetReference<BrainLibraryBlob> blob;
}

public struct BrainLibraryReference : IComponentData
{
    public UnityObjectRef<BrainLibrarySO> library;
}


