using Unity.Entities;
using UnityEngine;

public class BrainAuthoring : MonoBehaviour
{
    [SearchableEnum]
    [Tooltip("Which brain this unit starts with. Can be swapped at runtime via SwapBrainRequest.")]
    public BrainType brain;
    public bool active = true;
    public BrainLibrarySO brainLibrary;

    public class Baker : Baker<BrainAuthoring>
    {
        public override void Bake(BrainAuthoring authoring)
        {
            if (authoring.brainLibrary == null) return;

            BrainSO brainSO = authoring.brainLibrary.GetBrain(authoring.brain);
            if (brainSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            BrainUtil.BakeRequirements(this, entity, authoring.active, brainSO);
        }
    }
}
