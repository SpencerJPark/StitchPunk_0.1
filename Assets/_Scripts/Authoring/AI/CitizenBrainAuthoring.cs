using Unity.Entities;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public bool active = true;
    public UnitLibrarySO unitLibrary;

    [Tooltip("Optional empty hand socket child (EquiptSocketAuthoring) items attach to when equipped. " +
             "Leave null if this unit cannot visually hold items.")]
    public GameObject handSocket;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            if (authoring.unitLibrary == null) return;

            UnitSO unitSO = authoring.unitLibrary.GetUnitSO(UnitType.MaleCitizen);
            if (unitSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            UnitBakingUtil.BakeRequirements(this, entity, authoring.active, unitSO);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, MeleeSingleAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, WanderAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, TalkAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, SitAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, IdleAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, FleeAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, ReleaseRequest>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, InteractAction>(this, entity);
            UnitBakingUtil.AddAction<CitizenBrainAuthoring, PickupItemAction>(this, entity);

            Entity socketEntity = authoring.handSocket != null
                ? GetEntity(authoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquipt { socketEntity = socketEntity });
        }
    }
}