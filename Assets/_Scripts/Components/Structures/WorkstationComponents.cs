using Unity.Entities;
using Unity.Mathematics;

// The camp workstation (CampLoop_System.md §4). Faction-agnostic so a rival engineer's station
// (Phase 7) can reuse it with ownerPlayer = Entity.Null.
public struct Workstation : IComponentData
{
    public Entity ownerPlayer;
    public float3 mountLocalOffset;
    public float3 summonSpawnLocalOffset;
}

public struct WorkstationFurnace : IComponentData
{
    public bool isBurning;
    public float burnIntervalSeconds;
    public int electricityPerWood;
    public float burnTimer;
}

public struct WorkstationRecipes : IComponentData
{
    public int revivalRoundScrapCost;
    public int revivalRoundElectricityCost;
    public int repairWoodCost;
    public int repairHealthAmount;
}

// Which unit types this station offers, baked from WorkstationAuthoring.
public struct WorkstationSummonOption : IBufferElementData
{
    public UnitType unitType;
    public float buildSeconds;
}

// Appended by the station menu; WorkstationSummonSystem pays for, queues and clears these.
public struct WorkstationSummonRequest : IBufferElementData
{
    public UnitType unitType;
}

// Only the head entry ticks (one build at a time, CL-A8).
public struct WorkstationSummonQueueEntry : IBufferElementData
{
    public UnitType unitType;
    public float remainingSeconds;
    public float totalSeconds;
}

// Finished builds waiting for WorkstationSummonSpawnSystem (LateSimulation) to instantiate them.
public struct PendingMinionSpawn : IBufferElementData
{
    public UnitType unitType;
    public float3 position;
}

public struct CraftRevivalRoundRequest : IComponentData, IEnableableComponent
{
    public int count;
}

public struct WorkstationRepairRequest : IComponentData, IEnableableComponent { }

public struct WorkstationPackUpRequest : IComponentData, IEnableableComponent { }

public struct WorkstationFurnaceToggleRequest : IComponentData, IEnableableComponent { }
