using Unity.Entities;
using UnityEngine;

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


// public struct FactoryLibrary : IComponentData
// {
//     public BlobAssetReference<FactoryLibraryBlob> blob;
// }
//
// public struct FactoryLibraryReference : IComponentData
// {
//     public UnityObjectRef<FactoryLibrarySO> library;
// }


public struct InteractionLibrary : IComponentData
{
    public BlobAssetReference<InteractionLibraryBlob> library;
}

public struct InteractionLibraryReference : IComponentData
{
    public UnityObjectRef<InteractionLibrarySO> library;
}


// Utility AI v2 — behaviors library (enum-indexed by BehaviorType), referenced directly by systems.
public struct BehaviorLibrary : IComponentData
{
    public BlobAssetReference<BehaviorLibraryBlob> blob;
}

public struct BehaviorLibraryReference : IComponentData
{
    public UnityObjectRef<BehaviorLibrarySO> library;
}


// Utility AI v2 — brains + action defs. Singleton holding the master AIConfig blob.
public struct BrainLibrary : IComponentData
{
    public BlobAssetReference<BrainLibraryBlob> blob;
}

// Bake-only handoff: one entry per BrainConfigSO assigned on AIConfigAuthoring.
public struct BrainLibraryEntry : IBufferElementData
{
    public UnityObjectRef<BrainSO> config;
}


