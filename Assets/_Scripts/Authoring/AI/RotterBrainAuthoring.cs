using Unity.Entities;
using UnityEngine;

public class RotterBrainAuthoring : MonoBehaviour
{
    public bool active = true;
    public BrainLibrarySO brainLibrary;

    public class Baker : Baker<RotterBrainAuthoring>
    {
        public override void Bake(RotterBrainAuthoring authoring)
        {
            if (authoring.brainLibrary == null) return;

            BrainSO brainSO = authoring.brainLibrary.GetBrain(BrainType.Rotted);
            if (brainSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            BrainUtil.BakeRequirements(this, entity, authoring.active, brainSO);
            BrainUtil.AddAction<RotterBrainAuthoring, MeleeAction>(this, entity);
            BrainUtil.AddAction<RotterBrainAuthoring, WanderAction>(this, entity);
            BrainUtil.AddAction<RotterBrainAuthoring, IdleAction>(this, entity);
        }
    }
}