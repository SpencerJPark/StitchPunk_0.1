// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The GameObject stand-in the clip preview poses (architecture section 7.3, step 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One quad per rig target, posed every preview tick from the same <c>TargetPose</c> the runtime
    /// produces. Section 7.3 chose a GameObject mirror over an editor ECS world deliberately:
    /// Entities Graphics outside the default world is unsupported territory, and baking in edit mode
    /// is exactly the dependency the preview exists to avoid.
    /// </para>
    /// <para>
    /// <strong>This shows motion, not art.</strong> The auto-rig is untextured quads, so what it
    /// renders is the pose — timing, arcs, layer composition. Sprite slice values are still written
    /// into a <see cref="MaterialPropertyBlock"/> so that a caller which supplies a real material
    /// gets the right frame for free, but with the default material they are inert. Previewing the
    /// actual artwork is the actor-prefab route, which mirrors the authored prefab instead of
    /// building quads.
    /// </para>
    /// <para>
    /// Every object carries <see cref="HideFlags.HideAndDontSave"/>. Without it the mirror leaks
    /// into the user's open scene and, worse, gets saved into it.
    /// </para>
    /// </remarks>
    public sealed class PreviewRigMirror
    {
        private static readonly int ImageIndexPropertyId = Shader.PropertyToID("_ImageIndex");

        private GameObject rootObject;
        private readonly List<Transform> partTransforms = new List<Transform>();
        private readonly List<MeshRenderer> partRenderers = new List<MeshRenderer>();
        private readonly Dictionary<uint, int> targetIdToMirrorIndex = new Dictionary<uint, int>();
        private MaterialPropertyBlock propertyBlock;

        /// <summary>The mirror's root, or null when nothing is built.</summary>
        public GameObject RootObject
        {
            get { return rootObject; }
        }

        /// <summary>How many parts the mirror currently holds.</summary>
        public int PartCount
        {
            get { return partTransforms.Count; }
        }

        /// <summary>
        /// Rebuilds the mirror to match <paramref name="rig"/>. Safe to call repeatedly.
        /// </summary>
        public void Rebuild(RigAsset rig)
        {
            Dispose();

            if (rig == null || rig.targets == null || rig.targets.Count == 0)
            {
                return;
            }

            propertyBlock = new MaterialPropertyBlock();
            rootObject = new GameObject("ClipPreviewMirror");
            rootObject.hideFlags = HideFlags.HideAndDontSave;

            Material previewMaterial = UnityEditor.AssetDatabase
                .GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }

                // The rig's own id accessor, not the serialized field behind it — that one is
                // internal to the authoring assembly and invisible from here.
                uint targetId = target.Id.Value;

                GameObject partObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                partObject.name = string.IsNullOrEmpty(target.displayName)
                    ? "Target " + targetId.ToString()
                    : target.displayName;
                partObject.hideFlags = HideFlags.HideAndDontSave;
                partObject.transform.SetParent(rootObject.transform, false);

                // The collider would do nothing here but cost physics registration on every rebuild.
                Collider partCollider = partObject.GetComponent<Collider>();
                if (partCollider != null)
                {
                    Object.DestroyImmediate(partCollider);
                }

                MeshRenderer partRenderer = partObject.GetComponent<MeshRenderer>();
                if (partRenderer != null)
                {
                    partRenderer.sharedMaterial = previewMaterial;
                    partRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    partRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                targetIdToMirrorIndex[targetId] = partTransforms.Count;
                partTransforms.Add(partObject.transform);
                partRenderers.Add(partRenderer);
            }
        }

        /// <summary>
        /// Poses the part bound to <paramref name="targetId"/>. Unknown ids are ignored.
        /// </summary>
        /// <remarks>
        /// <paramref name="pose"/> carries a z-rotation in <em>radians</em> (the runtime's unit),
        /// while <see cref="Transform.localRotation"/> is built from degrees — the conversion here
        /// is the same one <c>TransformApplySystem</c> performs, and dropping it is how a preview
        /// silently runs at 57× the authored rotation.
        /// </remarks>
        public void ApplyPose(uint targetId, in TargetPose pose)
        {
            int mirrorIndex;
            if (!targetIdToMirrorIndex.TryGetValue(targetId, out mirrorIndex))
            {
                return;
            }

            Transform partTransform = partTransforms[mirrorIndex];
            partTransform.localPosition = new Vector3(
                pose.localPosition.x, pose.localPosition.y, pose.localPosition.z);
            partTransform.localRotation = Quaternion.Euler(0f, 0f, pose.rotationZ * Mathf.Rad2Deg);

            // z stays 1: the pose's 2D scale is the authored channel, and zeroing depth scale would
            // collapse the quad rather than leave it flat.
            partTransform.localScale = new Vector3(pose.scale.x, pose.scale.y, 1f);

            MeshRenderer partRenderer = partRenderers[mirrorIndex];
            if (partRenderer == null || propertyBlock == null)
            {
                return;
            }
            partRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(ImageIndexPropertyId, pose.sliceIndex);
            partRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>Destroys the mirror. Idempotent.</summary>
        public void Dispose()
        {
            if (rootObject != null)
            {
                Object.DestroyImmediate(rootObject);
                rootObject = null;
            }
            partTransforms.Clear();
            partRenderers.Clear();
            targetIdToMirrorIndex.Clear();
            propertyBlock = null;
        }
    }
}
