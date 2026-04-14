using Unity.Entities;
using Unity.Mathematics;

// Identity and grid position of a factory station
public struct FactoryStation : IComponentData
{
    public StructureType structureType;
    public int gridX;
    public int gridZ;
}

// Items waiting to be consumed by this station on the next production cycle
public struct StationInputSlot : IBufferElementData
{
    public FactoryItemType itemType;
}

// Items that have been produced and are ready to be picked up or forwarded
public struct StationOutputSlot : IBufferElementData
{
    public FactoryItemType itemType;
}

// Tracks an in-progress production cycle — enabled while a cycle is running, disabled otherwise
public struct ProductionProgress : IComponentData, IEnableableComponent
{
    public float elapsed;
    public int recipeBlobIndex;
}

// One entry per worker slot on this station — Entity.Null means the slot is empty
public struct StationWorkerSlot : IBufferElementData
{
    public Entity workerEntity;
}

// Singleton baked by FactoryGridAuthoring — defines the factory floor grid dimensions
public struct FactoryGridConfig : IComponentData
{
    public float cellSize;
    public int width;
    public int height;
    public float3 worldOrigin;
}

// One entry per grid cell (width * height total) — Entity.Null occupant means the cell is empty
public struct FactoryGridCell : IBufferElementData
{
    public Entity occupant;
}
