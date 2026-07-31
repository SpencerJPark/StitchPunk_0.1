// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Builds the authoring assets and GameObject hierarchies the M2 baking acceptance tests bake,
    /// and destroys every one of them again so nothing leaks between tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every stable id is assigned by hand rather than minted, so the canonical orderings the
    /// assertions quote are chosen by the test author instead of inferred from a random generator.
    /// The ids are deliberately out of authoring order: the rig lists Head, Torso, LeftArm while the
    /// dense target order is Torso (100), LeftArm (200), Head (300), and the set lists Idle before
    /// Walk while the dense clip order is Walk (0x10), Idle (0x20). A binding pass that returned
    /// authoring positions instead of resolving ids would pass on an in-order fixture and fail here.
    /// </para>
    /// <para>
    /// The rig's geometry is the other half of the point: <see cref="HeadLocalPosition"/> puts a part
    /// twelve units up the y axis, and <see cref="LeftArmLocalPosition"/> hangs a part off the torso
    /// rather than off the root, so the actor-space rest bounds can only be right if the baker walks
    /// the transform chain and never if it mistakes offset space for actor space.
    /// </para>
    /// <para>
    /// <see cref="TorsoLocalPosition"/> is deliberately off-axis in x. With the torso at x = 0 the
    /// left arm's actor-space x would equal its own local x, so a baker that never walked the chain
    /// would produce identical x bounds and the nested-part case would prove nothing. Offsetting the
    /// torso is what makes the chain observable on both axes.
    /// </para>
    /// </remarks>
    internal sealed class ActorBakeFixture
    {
        /// <summary>Dense target index 0 — the lowest target id in the fixture rig.</summary>
        internal const uint TorsoTargetId = 100u;

        /// <summary>Dense target index 1.</summary>
        internal const uint LeftArmTargetId = 200u;

        /// <summary>Dense target index 2 — the part placed far from the actor origin.</summary>
        internal const uint HeadTargetId = 300u;

        /// <summary>The test-only shader that declares the VAT texture slots.</summary>
        internal const string VatProbeShaderName = "Hidden/StitchPunk/AnimationToolkit/Tests/VatMaterialProbe";

        /// <summary>A target id the fixture rig deliberately does not declare.</summary>
        internal const uint UnknownTargetId = 999u;

        /// <summary>Expected dense index of <see cref="TorsoTargetId"/>.</summary>
        internal const int TorsoDenseIndex = 0;

        /// <summary>Expected dense index of <see cref="LeftArmTargetId"/>.</summary>
        internal const int LeftArmDenseIndex = 1;

        /// <summary>Expected dense index of <see cref="HeadTargetId"/>.</summary>
        internal const int HeadDenseIndex = 2;

        /// <summary>Clip id that sorts first, so its dense clip index is 0.</summary>
        internal const ulong WalkClipStableId = 0x0000000000000010UL;

        /// <summary>Clip id that sorts second, so its dense clip index is 1.</summary>
        internal const ulong IdleClipStableId = 0x0000000000000020UL;

        /// <summary>Expected dense clip index of the walk clip.</summary>
        internal const int WalkDenseClipIndex = 0;

        /// <summary>Expected dense clip index of the idle clip.</summary>
        internal const int IdleDenseClipIndex = 1;

        /// <summary>Number of playback layers the fixture rig declares.</summary>
        internal const int LayerCount = 2;

        /// <summary>Half-extents authored on the torso target.</summary>
        internal static readonly float3 TorsoBoundsExtents = new float3(0.5f, 0.75f, 0.1f);

        /// <summary>Half-extents authored on the left-arm target.</summary>
        internal static readonly float3 LeftArmBoundsExtents = new float3(0.25f, 0.5f, 0.1f);

        /// <summary>Half-extents authored on the head target.</summary>
        internal static readonly float3 HeadBoundsExtents = new float3(0.4f, 0.4f, 0.1f);

        /// <summary>Torso rest position, relative to the actor root.</summary>
        internal static readonly Vector3 TorsoLocalPosition = new Vector3(0.4f, 1f, 0f);

        /// <summary>Left-arm rest position, relative to the <em>torso</em>, not the root.</summary>
        internal static readonly Vector3 LeftArmLocalPosition = new Vector3(-0.6f, 0.3f, 0f);

        /// <summary>Head rest position, relative to the actor root — far enough out to expose an offset-space bug.</summary>
        internal static readonly Vector3 HeadLocalPosition = new Vector3(0f, 12f, 0f);

        private readonly List<Object> createdObjects = new List<Object>();

        // -----------------------------------------------------------------------------------
        // Authoring assets.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The three-target, two-layer rig every fixture actor animates. Layer 0 is active by
        /// default and layer 1 is not, so the seeded <c>PlaybackLayer</c> flags are distinguishable.
        /// </summary>
        internal RigAsset CreateRig(string assetName)
        {
            RigAsset rig = Create<RigAsset>(assetName);
            rig.stableId = 0x5000000000000001UL;

            rig.layers.Clear();
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.layers.Add(new LayerDefinition { displayName = "Upper", defaultActive = false });

            rig.targets.Clear();
            rig.targets.Add(CreateTarget("Head", HeadTargetId, TargetKind.Quad, HeadBoundsExtents));
            rig.targets.Add(CreateTarget("Torso", TorsoTargetId, TargetKind.Quad, TorsoBoundsExtents));
            rig.targets.Add(CreateTarget("LeftArm", LeftArmTargetId, TargetKind.Quad, LeftArmBoundsExtents));
            return rig;
        }

        /// <summary>The two-clip set the fixture actors reference, listed out of id order on purpose.</summary>
        internal ClipSetAsset CreateClipSet(string assetName, RigAsset rig, ulong setStableId)
        {
            ClipAsset idleClip = CreateClip("Idle", rig, IdleClipStableId, LoopMode.Loop);
            ClipAsset walkClip = CreateClip("Walk", rig, WalkClipStableId, LoopMode.Loop);

            ClipSetAsset clipSet = Create<ClipSetAsset>(assetName);
            clipSet.stableId = setStableId;
            clipSet.rig = rig;
            clipSet.clips.Clear();
            clipSet.clips.Add(idleClip);
            clipSet.clips.Add(walkClip);
            return clipSet;
        }

        /// <summary>Finds a clip in a fixture set by its stable id.</summary>
        internal static ClipAsset FindClip(ClipSetAsset clipSet, ulong clipStableId)
        {
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip != null && clip.Id.Value == clipStableId)
                {
                    return clip;
                }
            }
            return null;
        }

        private ClipAsset CreateClip(string assetName, RigAsset rig, ulong clipStableId, LoopMode defaultLoop)
        {
            ClipAsset clip = Create<ClipAsset>(assetName);
            clip.stableId = clipStableId;
            clip.rig = rig;
            clip.duration = 1f;
            clip.defaultLoop = defaultLoop;
            clip.defaultBlendIn = 0.1f;
            clip.defaultBlendOut = 0.1f;

            TransformTrack torsoTrack = new TransformTrack
            {
                targetId = TorsoTargetId,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.PositionXY
            };
            torsoTrack.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotationZ = 0f,
                scale = new float2(1f, 1f),
                interpolation = Interpolation.Linear
            });
            torsoTrack.keys.Add(new TransformKey
            {
                normalizedTime = 1f,
                position = new float3(0.25f, 0f, 0f),
                rotationZ = 0f,
                scale = new float2(1f, 1f),
                interpolation = Interpolation.Linear
            });
            clip.transformTracks.Add(torsoTrack);
            return clip;
        }

        private static RigTargetDefinition CreateTarget(
            string displayName,
            uint targetStableId,
            TargetKind kind,
            float3 boundsExtents)
        {
            return new RigTargetDefinition
            {
                displayName = displayName,
                stableId = targetStableId,
                kind = kind,
                boundsExtents = boundsExtents
            };
        }

        // -----------------------------------------------------------------------------------
        // GameObject hierarchies.
        // -----------------------------------------------------------------------------------

        /// <summary>Creates a bare actor root with no parts under it.</summary>
        internal GameObject CreateActorRoot(string name, ClipSetAsset clipSet, bool addDistanceLod)
        {
            GameObject actorGameObject = new GameObject(name);
            createdObjects.Add(actorGameObject);
            ActorAuthoring actorAuthoring = actorGameObject.AddComponent<ActorAuthoring>();
            actorAuthoring.clipSet = clipSet;
            actorAuthoring.addDistanceLod = addDistanceLod;
            return actorGameObject;
        }

        /// <summary>
        /// Creates the canonical three-part actor: torso and head under the root, left arm under the
        /// torso, layer 0 seeded with the walk clip.
        /// </summary>
        internal GameObject CreateStandardActor(string name, ClipSetAsset clipSet, bool addDistanceLod)
        {
            GameObject actorGameObject = CreateActorRoot(name, clipSet, addDistanceLod);
            GameObject torso = AddPart(actorGameObject, "Torso", TorsoTargetId, TorsoLocalPosition);
            AddPart(actorGameObject, "Head", HeadTargetId, HeadLocalPosition);
            AddPart(torso, "LeftArm", LeftArmTargetId, LeftArmLocalPosition);

            SeedStartingLayer(
                actorGameObject,
                0,
                FindClip(clipSet, WalkClipStableId),
                1f,
                LoopMode.UseClipDefault);
            return actorGameObject;
        }

        /// <summary>
        /// Finds a part GameObject by name under an actor. Tests need the specific object a dense
        /// index is expected to resolve to: asserting only that <em>some</em> part carries each
        /// index passes for any permutation, including the authoring-order bug this fixture exists
        /// to catch.
        /// </summary>
        internal static GameObject FindPart(GameObject actorGameObject, string partName)
        {
            Transform[] descendants = actorGameObject.GetComponentsInChildren<Transform>(true);
            for (int descendantIndex = 0; descendantIndex < descendants.Length; descendantIndex++)
            {
                if (descendants[descendantIndex].name == partName)
                {
                    return descendants[descendantIndex].gameObject;
                }
            }
            Assert.Fail("The fixture has no part named '" + partName + "'.");
            return null;
        }

        /// <summary>Adds a rig part under <paramref name="parent"/> at the given local position.</summary>
        internal GameObject AddPart(
            GameObject parent,
            string name,
            uint targetStableId,
            Vector3 localPosition)
        {
            GameObject partGameObject = new GameObject(name);
            partGameObject.transform.SetParent(parent.transform, false);
            partGameObject.transform.localPosition = localPosition;
            RigTargetAuthoring partAuthoring = partGameObject.AddComponent<RigTargetAuthoring>();
            partAuthoring.targetStableId = targetStableId;
            // The part deliberately leaves `rig` empty: inheriting the actor's rig is the documented
            // default and the configuration users will have.
            return partGameObject;
        }

        /// <summary>Adds one entry to an actor's starting-layer list.</summary>
        internal static void SeedStartingLayer(
            GameObject actorGameObject,
            int layerIndex,
            ClipAsset clip,
            float speed,
            LoopMode loop)
        {
            ActorAuthoring actorAuthoring = actorGameObject.GetComponent<ActorAuthoring>();
            actorAuthoring.startingLayers.Add(new StartingLayerState
            {
                layerIndex = layerIndex,
                clip = clip,
                speed = speed,
                loop = loop
            });
        }

        /// <summary>
        /// Creates a plain unlit material. It deliberately does <strong>not</strong> declare a
        /// <c>_VatBoneTex</c> slot — that absence is what the material-mismatch acceptance test
        /// detects. <paramref name="boneTexture"/> is bound to the shader's own <c>_MainTex</c>
        /// purely so the texture is referenced by something and not collected.
        /// </summary>
        internal Material CreateVatMaterial(string name, Texture2D boneTexture)
        {
            // A plain unlit material: it declares no _VatBoneTex slot at all. That absence is the
            // "wrong material assigned" case. boneTexture is bound to the shader's own _MainTex
            // purely so the texture is referenced by something.
            Shader shader = Shader.Find("Unlit/Texture");
            Material material = new Material(shader);
            material.name = name;
            createdObjects.Add(material);
            if (boneTexture != null)
            {
                material.SetTexture("_MainTex", boneTexture);
            }
            return material;
        }

        /// <summary>
        /// Creates a material that genuinely declares the VAT texture slots, bound to
        /// <paramref name="boneTexture"/>. Passing the texture the set baked gives a correctly
        /// configured part; passing a different one gives the architecture section 4.4 mismatch —
        /// the branch the acceptance list actually specifies, and the only one that can distinguish
        /// a validator from one that warns unconditionally.
        /// </summary>
        internal Material CreateVatCapableMaterial(string name, Texture2D boneTexture)
        {
            Shader shader = Shader.Find(VatProbeShaderName);
            Assert.IsNotNull(
                shader,
                "The test shader '" + VatProbeShaderName + "' did not import. Without it no fixture " +
                "can build a correctly configured VAT material, and the section 4.4 mismatch branch " +
                "cannot be reached at all.");
            Material material = new Material(shader);
            material.name = name;
            createdObjects.Add(material);
            if (boneTexture != null)
            {
                material.SetTexture("_VatBoneTex", boneTexture);
            }
            return material;
        }

        /// <summary>Creates a small readable texture the VAT texture-set fixtures point at.</summary>
        internal Texture2D CreateTexture(string name)
        {
            Texture2D texture = new Texture2D(2, 2);
            texture.name = name;
            createdObjects.Add(texture);
            return texture;
        }

        /// <summary>Creates a bone-flavor VAT texture set carrying <paramref name="boneTexture"/>.</summary>
        internal VatTextureSetAsset CreateVatTextureSet(string assetName, ulong setKey, Texture2D boneTexture)
        {
            VatTextureSetAsset vatTextureSet = Create<VatTextureSetAsset>(assetName);
            vatTextureSet.setKey = setKey;
            vatTextureSet.flavor = VatFlavor.BoneMatrix;
            vatTextureSet.boneTexture = boneTexture;
            vatTextureSet.boneCount = 2;
            vatTextureSet.textureWidth = 6;
            vatTextureSet.rowsPerFrame = 1;
            vatTextureSet.sourceHash = 0x1234567890ABCDEFUL;
            vatTextureSet.clipRanges.Clear();
            return vatTextureSet;
        }

        // -----------------------------------------------------------------------------------
        // Lifetime.
        // -----------------------------------------------------------------------------------

        /// <summary>Creates and registers a ScriptableObject of the requested type for later cleanup.</summary>
        internal T Create<T>(string assetName) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            createdObjects.Add(asset);
            return asset;
        }

        /// <summary>Destroys every object this fixture created. Safe to call more than once.</summary>
        internal void DestroyAll()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                Object createdObject = createdObjects[objectIndex];
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }
            createdObjects.Clear();
        }
    }
}
