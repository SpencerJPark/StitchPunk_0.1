using Unity.Entities;
using UnityEngine;

// Bakes the RagdollSimConfig singleton from a RagdollConfigSO (flat flatten — no blob; nothing is
// enum-indexed). Place ONE instance in the game subscene, next to the other config authorings.
// With no SO assigned (or no authoring in the scene at all) the ragdoll systems use the same
// defaults the SO fields declare, so the scene wiring is a tuning step, not a functional gate.
public class RagdollSimConfigAuthoring : MonoBehaviour
{
    [Tooltip("Global ragdoll simulation tuning. Null = bake the built-in defaults.")]
    public RagdollConfigSO config;

    public class Baker : Baker<RagdollSimConfigAuthoring>
    {
        public override void Bake(RagdollSimConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            RagdollSimConfig simConfig = new RagdollSimConfig
            {
                gravity               = 20f,
                horizontalDrag        = 2.5f,
                defaultRestitution    = 0.3f,
                bounceMinSpeed        = 2f,
                groundRaycastDistance = 5f,
                landingImpulseScale   = 1f,
                flailDamping          = 1.5f,
                sleepAngularSpeedDeg  = 1f,
                corpseCellSize        = 1f,
                corpseStackOffset     = 0.15f,
                corpseStackMax        = 5,
            };

            if (authoring.config != null)
            {
                DependsOn(authoring.config);
                simConfig.gravity               = authoring.config.gravity;
                simConfig.horizontalDrag        = authoring.config.horizontalDrag;
                simConfig.defaultRestitution    = authoring.config.defaultRestitution;
                simConfig.bounceMinSpeed        = authoring.config.bounceMinSpeed;
                simConfig.groundRaycastDistance = authoring.config.groundRaycastDistance;
                simConfig.landingImpulseScale   = authoring.config.landingImpulseScale;
                simConfig.flailDamping          = authoring.config.flailDamping;
                simConfig.sleepAngularSpeedDeg  = authoring.config.sleepAngularSpeedDeg;
                simConfig.corpseCellSize        = authoring.config.corpseCellSize;
                simConfig.corpseStackOffset     = authoring.config.corpseStackOffset;
                simConfig.corpseStackMax        = authoring.config.corpseStackMax;
            }

            AddComponent(entity, simConfig);
        }
    }
}
