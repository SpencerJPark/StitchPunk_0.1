using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class WorkstationAuthoring : MonoBehaviour
{
    [Header("Points")]
    [Tooltip("Where the mounted player stands. Defaults to (0,0,-1.5) when empty.")]
    public Transform mountPoint;
    [Tooltip("Where finished minions appear. Defaults to (0,0,2) when empty.")]
    public Transform summonSpawnPoint;
    [Tooltip("Centre of the formation summoned minions walk to. Defaults to (0,0,5) when empty.")]
    public Transform rallyPoint;

    [Header("Health")]
    public int maxHealth = 200;

    [Header("Furnace")]
    public bool furnaceStartsBurning = true;
    public float burnIntervalSeconds = 5f;
    public int electricityPerWood = 2;

    [Header("Recipes")]
    public int revivalRoundScrapCost = 1;
    public int revivalRoundElectricityCost = 1;
    public int repairWoodCost = 2;
    public int repairHealthAmount = 25;

    [Header("Summoning")]
    public SummonOptionAuthoring[] summonOptions = new SummonOptionAuthoring[]
    {
        new SummonOptionAuthoring { unitType = UnitType.PlayerZombie, buildSeconds = 4f },
        new SummonOptionAuthoring { unitType = UnitType.PlayerZombieBrute, buildSeconds = 8f },
        new SummonOptionAuthoring { unitType = UnitType.PlayerZombieRunner, buildSeconds = 3f },
    };

    [System.Serializable]
    public struct SummonOptionAuthoring
    {
        [SearchableEnum] public UnitType unitType;
        public float buildSeconds;
    }

    public class Baker : Baker<WorkstationAuthoring>
    {
        public override void Bake(WorkstationAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            float3 mountLocalOffset = authoring.mountPoint != null
                ? (float3)authoring.transform.InverseTransformPoint(authoring.mountPoint.position)
                : new float3(0f, 0f, -1.5f);
            float3 summonSpawnLocalOffset = authoring.summonSpawnPoint != null
                ? (float3)authoring.transform.InverseTransformPoint(authoring.summonSpawnPoint.position)
                : new float3(0f, 0f, 2f);
            float3 rallyLocalOffset = authoring.rallyPoint != null
                ? (float3)authoring.transform.InverseTransformPoint(authoring.rallyPoint.position)
                : new float3(0f, 0f, 5f);

            AddComponent(entity, new Workstation
            {
                ownerPlayer = Entity.Null,
                mountLocalOffset = mountLocalOffset,
                summonSpawnLocalOffset = summonSpawnLocalOffset,
                rallyLocalOffset = rallyLocalOffset,
            });
            AddComponent(entity, new WorkstationFurnace
            {
                isBurning = authoring.furnaceStartsBurning,
                burnIntervalSeconds = authoring.burnIntervalSeconds,
                electricityPerWood = authoring.electricityPerWood,
                burnTimer = 0f,
            });
            AddComponent(entity, new WorkstationRecipes
            {
                revivalRoundScrapCost = authoring.revivalRoundScrapCost,
                revivalRoundElectricityCost = authoring.revivalRoundElectricityCost,
                repairWoodCost = authoring.repairWoodCost,
                repairHealthAmount = authoring.repairHealthAmount,
            });

            DynamicBuffer<WorkstationSummonOption> optionBuffer = AddBuffer<WorkstationSummonOption>(entity);
            if (authoring.summonOptions != null)
            {
                for (int optionIndex = 0; optionIndex < authoring.summonOptions.Length; optionIndex++)
                {
                    optionBuffer.Add(new WorkstationSummonOption
                    {
                        unitType = authoring.summonOptions[optionIndex].unitType,
                        buildSeconds = authoring.summonOptions[optionIndex].buildSeconds,
                    });
                }
            }
            AddBuffer<WorkstationSummonRequest>(entity);
            AddBuffer<WorkstationSummonQueueEntry>(entity);
            AddBuffer<PendingMinionSpawn>(entity);
            AddBuffer<WorkstationRallyClaim>(entity);

            AddComponent<CraftRevivalRoundRequest>(entity);
            SetComponentEnabled<CraftRevivalRoundRequest>(entity, false);
            AddComponent<WorkstationRepairRequest>(entity);
            SetComponentEnabled<WorkstationRepairRequest>(entity, false);
            AddComponent<WorkstationPackUpRequest>(entity);
            SetComponentEnabled<WorkstationPackUpRequest>(entity, false);
            AddComponent<WorkstationFurnaceToggleRequest>(entity);
            SetComponentEnabled<WorkstationFurnaceToggleRequest>(entity, false);

            AddComponent(entity, new Health
            {
                healthAmount = authoring.maxHealth,
                healthAmountMax = authoring.maxHealth,
            });
            AddComponent(entity, new Faction { factionType = FactionType.Player });
            AddComponent<PlayerInteractable>(entity);
        }
    }
}
