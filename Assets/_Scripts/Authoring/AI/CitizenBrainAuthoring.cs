using Unity.Entities;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public bool active = true;
    public UnitLibrarySO unitLibrary;

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
        }
    }
}