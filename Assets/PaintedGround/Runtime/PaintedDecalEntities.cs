// Entities Graphics support for Painted/Decal.
//
// Entities Graphics ignores MaterialPropertyBlocks, so the per-decal tile and tint are baked
// into these two components, which map to the shader's DOTS-instanced _Tile and _Tint.
//
// This file only compiles when PAINTED_ENTITIES is defined, so the package works in projects
// without the Entities packages. To enable:
//   1. Install com.unity.entities.graphics (pulls in com.unity.entities).
//   2. Project Settings > Player > Scripting Define Symbols: add  PAINTED_ENTITIES
//   3. Put the ground, water and decals in a SubScene. Keep the camera and lights outside
//      (CampfireFlicker is a MonoBehaviour and does not run on baked entities).
//
// Everything else (ground, water) needs no code: the shaders carry a DOTS_INSTANCING_ON
// variant and bake through the standard MeshRenderer baker.

#if PAINTED_ENTITIES
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace PaintedGround
{
    [MaterialProperty("_Tile")]
    public struct PaintedDecalTile : IComponentData
    {
        public float Value;
    }

    [MaterialProperty("_Tint")]
    public struct PaintedDecalTint : IComponentData
    {
        public float4 Value;
    }

    public class PaintedDecalBaker : Baker<PaintedDecal>
    {
        public override void Bake(PaintedDecal authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            var c = authoring.tint; c.a *= authoring.opacity;
            AddComponent(entity, new PaintedDecalTile { Value = authoring.tile });
            AddComponent(entity, new PaintedDecalTint { Value = new float4(c.r, c.g, c.b, c.a) });
        }
    }
}
#endif
