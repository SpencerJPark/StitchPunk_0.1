using Unity.Entities;
using Unity.Mathematics;

// Wind-up aim lock (SmartHumanAI H5-D1). The WindUp command enables it and locks aimPoint to the target's position on its
// first frame; RequestThrow/RequestShoot fire at aimPoint and disable it, so stepping aside during the wind-up dodges.
public struct RangedAim : IComponentData, IEnableableComponent
{
    public float3 aimPoint;
    public float  windUpTotal;
    public float  windUpRemaining;
}

// Wind-up look tunables (H5-D2/D3), baked from WindUpPresentationAuthoring on the game manager; the owner judges them from a capture.
public struct WindUpPresentationConfig : IComponentData
{
    public float4 flashColor;     // rgb = flash colour; the shader lerps the lit colour toward it by BodyPartFlash.Value.a
    public float  flashMaximum;   // 0.55
    public float  scaleMaximum;   // 1.10
}
