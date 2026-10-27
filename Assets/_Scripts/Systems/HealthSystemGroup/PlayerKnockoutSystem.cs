using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Not Burst: Debug.Log. Runs before DeathSystem so Dead never stays enabled on the player.
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(DeathSystem))]
public partial struct PlayerKnockoutSystem : ISystem
{
    private const float KnockoutSeconds = 5f;
    private const float WakeSidewaysOffset = 1.5f;

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity player))
            return;
        EntityManager entityManager = state.EntityManager;
        if (!entityManager.HasComponent<Health>(player) || !entityManager.HasComponent<PlayerKnockedOut>(player))
            return;

        // Damage enables Dead for the player too; knockout replaces death, so always clear it.
        if (entityManager.HasComponent<Dead>(player) && entityManager.IsComponentEnabled<Dead>(player))
            entityManager.SetComponentEnabled<Dead>(player, false);

        Health health = entityManager.GetComponentData<Health>(player);

        if (entityManager.IsComponentEnabled<PlayerKnockedOut>(player))
        {
            if (entityManager.IsComponentEnabled<MovePlayerInput>(player))
                entityManager.SetComponentEnabled<MovePlayerInput>(player, false);

            PlayerKnockedOut knockedOut = entityManager.GetComponentData<PlayerKnockedOut>(player);
            knockedOut.remainingSeconds -= SystemAPI.Time.DeltaTime;
            if (knockedOut.remainingSeconds <= 0f)
            {
                health.healthAmount = health.healthAmountMax;
                entityManager.SetComponentData(player, health);
                entityManager.SetComponentEnabled<PlayerKnockedOut>(player, false);
                Debug.Log("[PlayerKnockout] Player woke up at the workstation with full health.");
            }
            else
            {
                entityManager.SetComponentData(player, knockedOut);
            }
            return;
        }

        if (health.healthAmount > 0)
            return;

        Entity workstation = entityManager.HasComponent<PlayerWorkstationLink>(player)
            ? entityManager.GetComponentData<PlayerWorkstationLink>(player).workstation
            : Entity.Null;
        bool workstationExists = workstation != Entity.Null
            && entityManager.Exists(workstation)
            && entityManager.HasComponent<Workstation>(workstation)
            && entityManager.HasComponent<LocalTransform>(workstation);
        // A broken caravan is no place to wake up: going down beside one is the loss (Caravan_System.md CV-D14).
        bool caravanBroken = workstationExists
            && entityManager.HasComponent<CaravanBroken>(workstation)
            && entityManager.IsComponentEnabled<CaravanBroken>(workstation);

        if (!workstationExists || caravanBroken)
        {
            // No respawn point: game over. The singleton is baked disabled, so a plain singleton query would throw.
            EntityQuery gameOverQuery = SystemAPI.QueryBuilder()
                .WithPresent<CampLoopGameOver>()
                .Build();
            using (NativeArray<Entity> gameOverEntities = gameOverQuery.ToEntityArray(Allocator.Temp))
            {
                if (gameOverEntities.Length > 0)
                    entityManager.SetComponentEnabled<CampLoopGameOver>(gameOverEntities[0], true);
            }
            Debug.Log(caravanBroken
                ? "[PlayerKnockout] Player down while the caravan is broken: game over."
                : "[PlayerKnockout] Player down with no workstation deployed: game over.");
            return;
        }

        if (entityManager.HasComponent<WorkstationMounted>(player)
            && entityManager.IsComponentEnabled<WorkstationMounted>(player))
            entityManager.SetComponentEnabled<WorkstationMounted>(player, false);

        LocalTransform stationTransform = entityManager.GetComponentData<LocalTransform>(workstation);
        Workstation stationData = entityManager.GetComponentData<Workstation>(workstation);
        float3 wakePosition = stationTransform.Position
            + math.rotate(stationTransform.Rotation, stationData.summonSpawnLocalOffset)
            + math.rotate(stationTransform.Rotation, new float3(WakeSidewaysOffset, 0f, 0f));

        LocalTransform playerTransform = entityManager.GetComponentData<LocalTransform>(player);
        playerTransform.Position = wakePosition;
        entityManager.SetComponentData(player, playerTransform);

        entityManager.SetComponentData(player, new PlayerKnockedOut { remainingSeconds = KnockoutSeconds });
        entityManager.SetComponentEnabled<PlayerKnockedOut>(player, true);
        Debug.Log("[PlayerKnockout] Player knocked out; waking at the workstation in 5 seconds.");
    }
}
