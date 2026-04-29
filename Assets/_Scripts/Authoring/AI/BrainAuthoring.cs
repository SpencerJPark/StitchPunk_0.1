using Unity.Entities;
using UnityEngine;

public class BrainAuthoring : MonoBehaviour
{
    [SearchableEnum]
    [Tooltip("Which unit configuration this entity uses. Can be swapped at runtime via SwapBrainRequest.")]
    public UnitType unitType;
    public bool active = true;
    public UnitLibrarySO unitLibrary;

    public class Baker : Baker<BrainAuthoring>
    {
        public override void Bake(BrainAuthoring authoring)
        {
            if (authoring.unitLibrary == null) return;

            UnitSO unitSO = authoring.unitLibrary.GetUnitSO(authoring.unitType);
            if (unitSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            UnitBakingUtil.BakeRequirements(this, entity, authoring.active, unitSO);
        }
    }
}
