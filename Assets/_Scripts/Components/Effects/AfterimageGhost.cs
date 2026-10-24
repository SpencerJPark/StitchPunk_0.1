using Unity.Entities;
using Unity.Mathematics;

// A frozen copy of one rendered visual entity, left behind by a roll. AfterimageFadeSystem shrinks
// and fades it; Lifetime -> Despawn destroys it.
public struct AfterimageGhost : IComponentData
{
    public float age;
    public float lifetime;
    public float4x4 startPose;
    public float4 startTint;
    public bool hasTint;
}
