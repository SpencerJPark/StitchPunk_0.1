using Unity.Entities;
using UnityEngine;

public class RotterBrainAuthoring : MonoBehaviour
{
    public bool active = true;
    public UnitLibrarySO unitLibrary;

    public class Baker : Baker<RotterBrainAuthoring>
    {
        public override void Bake(RotterBrainAuthoring authoring)
        {
            if (authoring.unitLibrary == null) return;

            UnitSO unitSO = authoring.unitLibrary.GetUnitSO(UnitType.MaleRotter);
            if (unitSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            UnitBakingUtil.BakeRequirements(this, entity, authoring.active, unitSO);
            UnitBakingUtil.AddAction<RotterBrainAuthoring, MeleeContinuousAction>(this, entity);
            UnitBakingUtil.AddAction<RotterBrainAuthoring, WanderAction>(this, entity);
            UnitBakingUtil.AddAction<RotterBrainAuthoring, IdleAction>(this, entity);
        }
    }
}
