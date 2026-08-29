using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

// One per part GameObject in a character rig — quad or item socket. Replaces
// AnimationTargetAuthoring + AnimationTargetNoIndexAuthoring + BaseParentAuthoring on rig parts:
//   • bakes BodyPartInfo (self-description: target + partDef + role flags),
//   • bakes BaseParent so the root can rebuild its BodyPart buffer after Instantiate,
//   • bakes PostTransformMatrix (non-uniform scale support) and the tint components for every part.
// CharacterRigBakingSystem assembles the root's BodyPart buffer from these. Ragdoll bodies are
// authored directly on the toolkit's RigAsset (Clip Editor) — nothing for this baker to flag. Rest
// pose and rest sprite slice are the toolkit's own RigTargetAuthoring (added alongside this one) —
// not this baker's job any more.
public class BodyPartAuthoring : MonoBehaviour
{
    [Tooltip("Which body part this GameObject is. Stays the single part-identity key everywhere.")]
    public AnimationTarget target;

    [Tooltip("The character root GameObject this part belongs to (baked into BaseParent).")]
    public GameObject characterRoot;

    [FormerlySerializedAs("partDef")]
    [Tooltip("Static config for this part KIND (design grid + ragdoll zones). Optional — leave null " +
             "for parts with no design variants and no ragdoll config; the part still animates.")]
    public UnitPartSO unitPartDef;

    [Tooltip("Per-instance multiply tint for this part's sprite (drives _BaseColor). White = authored " +
             "colour unchanged; black outline survives (0 * tint = 0). Ignored for non-rendering parts. " +
             "Placeholder until a global palette/skin system writes the colour.")]
    public Color tintColor = Color.white;

    [Tooltip("This part's packed mask genuinely uses the G/B layer channels (e.g. a bloody-mouth " +
             "overlay). Unchecked (default) bakes the secondary/tertiary layer tints with ALPHA 0 — " +
             "PackedChannelRecolor treats alpha as layer blend strength, so stray G/B data in the " +
             "texture can never composite. Palette designs that write those slots re-activate the " +
             "layers regardless (their colour alpha wins at apply).")]
    public bool useLayerChannels;

    [Tooltip("This part is an item attach socket (e.g. ItemLeftHand). Flagged in the BodyPart buffer.")]
    public bool isItemSocket;

    public class Baker : Baker<BodyPartAuthoring>
    {
        public override void Bake(BodyPartAuthoring authoring)
        {
            bool hasRenderer = authoring.GetComponent<Renderer>() != null;

            Entity entity = GetEntity(hasRenderer
                ? TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale
                : TransformUsageFlags.Dynamic);

            UnitPartId unitPartId = UnitPartId.None;
            if (authoring.unitPartDef != null)
            {
                DependsOn(authoring.unitPartDef);
                unitPartId = authoring.unitPartDef.id;
            }

            BodyPartFlags flags = BodyPartFlags.None;
            if (hasRenderer)                     flags |= BodyPartFlags.HasQuad;
            if (authoring.unitPartDef != null)   flags |= BodyPartFlags.DesignSlot;
            if (authoring.isItemSocket)          flags |= BodyPartFlags.ItemSocket;

            AddComponent(entity, new BodyPartInfo
            {
                target  = authoring.target,
                unitPart = unitPartId,
                flags   = flags,
            });

            if (authoring.characterRoot != null)
            {
                Entity rootEntity = GetEntity(authoring.characterRoot, TransformUsageFlags.Dynamic);
                AddComponent(entity, new BaseParent { baseParentEntity = rootEntity });
            }

            AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });

            // Camera-visibility gate (starts visible; CameraVisibilitySystem propagates the root's
            // state here so part-level presentation jobs can chunk-filter on it).
            AddComponent<CameraVisible>(entity);
            SetComponentEnabled<CameraVisible>(entity, true);

            if (hasRenderer)
            {
                // Facing (DirectionFacing_System.md §4): opt-in in the toolkit, so the game bakes it
                // wherever turning should apply — every quad part. Written only by UnitFacingSystem.
                AddComponent(entity, new PartFacing { viewOffset = 0, mirrorX = false });

                // Per-instance tint (drives _BaseColor, Hybrid Per Instance). Set per-part in the
                // authoring inspector; white leaves the authored sprite unchanged. A future global
                // palette/skin system will overwrite this at runtime.
                // Convert sRGB → linear: the DOTS MaterialProperty upload is raw (unlike the material
                // inspector, which auto-converts colour properties), and the project renders in Linear.
                Color linearTint = authoring.tintColor.linear;
                AddComponent(entity, new BodyPartTint
                {
                    Value = new float4(linearTint.r, linearTint.g, linearTint.b, linearTint.a),
                });
                // Packed-recolor layer tints (_SecondaryColor/_TertiaryColor). Alpha = layer blend
                // strength in PackedChannelRecolor: parts whose masks don't use G/B bake alpha 0 so
                // stray channel data (e.g. greyscale sources copied into every channel) can never
                // composite. DesignApplyUtil.ApplyDesign writes palette colours over these on
                // design-slot parts; keeping the components on every rendering part keeps one
                // batchable archetype.
                float layerAlpha = authoring.useLayerChannels ? 1f : 0f;
                AddComponent(entity, new BodyPartSecondaryTint { Value = new float4(1f, 1f, 1f, layerAlpha) });
                AddComponent(entity, new BodyPartTertiaryTint { Value = new float4(1f, 1f, 1f, layerAlpha) });
            }
        }
    }
}
