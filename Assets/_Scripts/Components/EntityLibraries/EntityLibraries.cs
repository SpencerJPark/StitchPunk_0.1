using Unity.Entities;
using UnityEngine;


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


public struct InteractionLibrary : IComponentData
{
    public BlobAssetReference<InteractionLibraryBlob> library;
}

public struct InteractionLibraryReference : IComponentData
{
    public UnityObjectRef<InteractionLibrarySO> library;
}


public struct BehaviorLibrary : IComponentData
{
    public BlobAssetReference<BehaviorLibraryBlob> blob;
}

public struct BehaviorLibraryReference : IComponentData
{
    public UnityObjectRef<BehaviorLibrarySO> library;
}


public struct BrainLibrary : IComponentData
{
    public BlobAssetReference<BrainLibraryBlob> blob;
}

// Bake-only handoff: one entry per BrainConfigSO assigned on AIConfigAuthoring.
public struct BrainLibraryEntry : IBufferElementData
{
    public UnityObjectRef<BrainSO> config;
}


// Single source of truth for every colour in the game; read by DesignRandomizeSystem and DesignApplyUtil.ResolveColor.
public struct ColorPaletteLibrary : IComponentData
{
    public BlobAssetReference<ColorPaletteLibraryBlob> blob;
}

public struct ColorPaletteLibraryReference : IComponentData
{
    public UnityObjectRef<ColorPaletteLibrarySO> library;
}


public struct AnimSoundEventLibrary : IComponentData
{
    public BlobAssetReference<AnimSoundEventMappingBlob> blob;
}

public struct AnimSoundEventLibraryReference : IComponentData
{
    public UnityObjectRef<AnimSoundEventMappingSO> library;
}

public struct VehicleLibrary : IComponentData
{
    public BlobAssetReference<VehicleLibraryBlob> library;
}

public struct VehicleLibraryReference : IComponentData
{
    public UnityObjectRef<VehicleLibrarySO> library;
}

// Lives on the TradeBoard entity.
public struct ClientLibrary : IComponentData
{
    public BlobAssetReference<ClientLibraryBlob> library;
}

public struct ClientLibraryReference : IComponentData
{
    public UnityObjectRef<ClientLibrarySO> library;
}
