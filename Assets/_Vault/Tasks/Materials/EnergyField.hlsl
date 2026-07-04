// Example 3 — Procedural energy / lava field (Unity 6.5+)
//
// Two things this example shows off that the first two didn't:
//
//   1. Helper functions. Only the function marked with UNITY_EXPORT_REFLECTION
//      becomes a node. Everything else in the file (Hash, ValueNoise, Fbm) is
//      just normal HLSL that the exported function is free to call. This is how
//      you wrap noise, SDFs, or SRP Shader Library functions behind one clean node.
//
//   2. Multiple outputs via 'out' parameters. The reflection API supports
//      in / out / inout parameters and a void return type, so a single node can
//      drive both Base Color and Emission on the master stack.

#include "ShaderApiReflectionSupport.hlsl"

// --- Helper functions (not reflected, just plain HLSL) ----------------------

float Hash(float2 p) {
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise(float2 uv) {
    float2 i = floor(uv);
    float2 f = frac(uv);
    float a = Hash(i);
    float b = Hash(i + float2(1, 0));
    float c = Hash(i + float2(0, 1));
    float d = Hash(i + float2(1, 1));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float Fbm(float2 uv) {
    float sum = 0.0;
    float amp = 0.5;
    [unroll]
    for (int i = 0; i < 5; i++) {
        sum += amp * ValueNoise(uv);
        uv *= 2.0;
        amp *= 0.5;
    }
    return sum;
}

// --- The reflected node -----------------------------------------------------

///<funchints>
///     <sg:ProviderKey>Malevolent.EnergyField</sg:ProviderKey>
///     <sg:DisplayName>Energy Field</sg:DisplayName>
///     <sg:SearchCategory>Malevolent/Procedural</sg:SearchCategory>
///</funchints>
///<paramhints name = "coolColor">
///     <sg:DisplayName>Cool Color</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.05, 0.0, 0.2</sg:Default>
///</paramhints>
///<paramhints name = "hotColor">
///     <sg:DisplayName>Hot Color</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1.0, 0.45, 0.1</sg:Default>
///</paramhints>
///<paramhints name = "scale">
///     <sg:DisplayName>Scale</sg:DisplayName>
///     <sg:Range>1, 16</sg:Range>
///     <sg:Default>4</sg:Default>
///</paramhints>
///<paramhints name = "speed">
///     <sg:DisplayName>Speed</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>0.5</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void EnergyField(
    float2 uv,
    float time,
    float3 coolColor,
    float3 hotColor,
    float scale,
    float speed,
    out float3 albedo,
    out float3 emission) {
    float n = Fbm(uv * scale + time * speed);
    float ramp = saturate(n * n * 2.0);

    albedo = lerp(coolColor, hotColor, ramp);
    emission = hotColor * pow(ramp, 3.0) * 4.0;
}