using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Physics.Systems;
using UnityEngine;

// Physics and the two toolkit groups are not GameSceneSystemGroups, so the hold has to switch them off directly (SaveAndTitle SP-4).
[UpdateInGroup(typeof(GameManagerSystemGroup), OrderFirst = true)]
public partial class SimulationHoldSystem : SystemBase
{
    private EntityQuery simulationHoldQuery;
    private bool appliedHoldActive;
    private PhysicsSystemGroup physicsSystemGroup;
    private MovementSystemGroup movementSystemGroup;
    private AnimationToolkitSystemGroup animationToolkitSystemGroup;

    protected override void OnCreate()
    {
        // CreateEntityQuery, not GetEntityQuery: a registered query would stop this system updating when the hold is removed.
        simulationHoldQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationHold>());
    }

    protected override void OnUpdate()
    {
        if (physicsSystemGroup == null)
        {
            physicsSystemGroup = World.GetExistingSystemManaged<PhysicsSystemGroup>();
        }
        if (movementSystemGroup == null)
        {
            movementSystemGroup = World.GetExistingSystemManaged<MovementSystemGroup>();
        }
        if (animationToolkitSystemGroup == null)
        {
            animationToolkitSystemGroup = World.GetExistingSystemManaged<AnimationToolkitSystemGroup>();
        }

        bool holdActive = !simulationHoldQuery.IsEmptyIgnoreFilter;
        if (holdActive == appliedHoldActive)
        {
            return;
        }
        appliedHoldActive = holdActive;
        ApplyEnabled(!holdActive);
        Debug.Log($"[SimulationHold] {(holdActive ? "Holding" : "Released")} the simulation");
    }

    protected override void OnDestroy()
    {
        if (appliedHoldActive)
        {
            ApplyEnabled(true);
        }
    }

    private void ApplyEnabled(bool enabledValue)
    {
        if (physicsSystemGroup != null)
        {
            physicsSystemGroup.Enabled = enabledValue;
        }
        if (movementSystemGroup != null)
        {
            movementSystemGroup.Enabled = enabledValue;
        }
        if (animationToolkitSystemGroup != null)
        {
            animationToolkitSystemGroup.Enabled = enabledValue;
        }
    }
}
