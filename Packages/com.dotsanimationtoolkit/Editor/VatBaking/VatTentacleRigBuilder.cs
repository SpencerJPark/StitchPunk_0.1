// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Builds a procedural tentacle — a tall strip with a bone chain down its length — as the
    /// reference subject for VAT baking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a tentacle and not a humanoid.</strong> It is the shape this system is actually
    /// for: the owner describes the whole animation model as "spline but for ECS", and a bending
    /// strip is a spline made visible. It also fails loudly — a chain that bends wrongly looks
    /// obviously wrong, where a humanoid arm off by a few degrees looks like a slightly different
    /// pose. For a first VAT bake, a subject that cannot fail subtly is worth more than a realistic
    /// one.
    /// </para>
    /// <para>
    /// It is procedural rather than an imported asset so the package carries no binary test content
    /// and anyone can regenerate it. The clip is a travelling sine down the chain, which exercises
    /// the two things a VAT bake must get right: every bone moving independently, and the pose
    /// differing on every frame so a bake that sampled once is immediately visible as a stiff rod.
    /// </para>
    /// </remarks>
    public static class VatTentacleRigBuilder
    {
        private const int SegmentCount = 12;
        private const float SegmentLength = 0.25f;
        private const float TentacleWidth = 0.22f;
        private const int WaveSampleRate = 30;
        private const float WaveDurationSeconds = 2f;

        /// <summary>
        /// Creates the rig in the current scene and returns its <see cref="SkinnedMeshRenderer"/>.
        /// </summary>
        public static SkinnedMeshRenderer CreateTentacle(string name, out AnimationClip waveClip)
        {
            GameObject root = new GameObject(name);

            // The bone chain: each bone a child of the last, so rotating one carries everything
            // above it. That parenting IS the spline — a chain of local rotations integrating into
            // a curve.
            Transform[] bones = new Transform[SegmentCount];
            Transform parent = root.transform;
            for (int boneIndex = 0; boneIndex < SegmentCount; boneIndex++)
            {
                GameObject bone = new GameObject("Bone" + boneIndex.ToString());
                bone.transform.SetParent(parent, false);
                bone.transform.localPosition = boneIndex == 0
                    ? Vector3.zero
                    : new Vector3(0f, SegmentLength, 0f);
                bones[boneIndex] = bone.transform;
                parent = bone.transform;
            }

            Mesh mesh = BuildStripMesh(bones, root.transform);

            GameObject meshObject = new GameObject("TentacleMesh");
            meshObject.transform.SetParent(root.transform, false);

            SkinnedMeshRenderer renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.updateWhenOffscreen = true;

            waveClip = BuildWaveClip();
            return renderer;
        }

        /// <summary>
        /// A two-vertex-wide strip running up Y, skinned to the chain.
        /// </summary>
        /// <remarks>
        /// Each ring of vertices is weighted between the two bones it sits between, linearly by how
        /// far along the segment it is. Two influences is enough for a chain and keeps the bake
        /// inside the two-influence budget §12 R3 recommends for crowds on constrained hardware.
        /// </remarks>
        private static Mesh BuildStripMesh(Transform[] bones, Transform rootTransform)
        {
            int ringCount = SegmentCount + 1;
            Vector3[] vertices = new Vector3[ringCount * 2];
            Vector3[] normals = new Vector3[ringCount * 2];
            Vector2[] uvs = new Vector2[ringCount * 2];
            BoneWeight[] boneWeights = new BoneWeight[ringCount * 2];

            for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
            {
                float height = ringIndex * SegmentLength;
                float alongChain = (float)ringIndex / SegmentCount;

                // Taper toward the tip so the shape reads as a tentacle rather than a plank, and so
                // a twist in the bake is visible in the silhouette.
                float halfWidth = TentacleWidth * (1f - alongChain * 0.7f) * 0.5f;

                int leftIndex = ringIndex * 2;
                int rightIndex = leftIndex + 1;

                vertices[leftIndex] = new Vector3(-halfWidth, height, 0f);
                vertices[rightIndex] = new Vector3(halfWidth, height, 0f);
                normals[leftIndex] = Vector3.back;
                normals[rightIndex] = Vector3.back;
                uvs[leftIndex] = new Vector2(0f, alongChain);
                uvs[rightIndex] = new Vector2(1f, alongChain);

                int lowerBone = Mathf.Clamp(ringIndex - 1, 0, SegmentCount - 1);
                int upperBone = Mathf.Clamp(ringIndex, 0, SegmentCount - 1);

                BoneWeight weight = new BoneWeight();
                weight.boneIndex0 = lowerBone;
                weight.boneIndex1 = upperBone;
                weight.weight0 = lowerBone == upperBone ? 1f : 0.5f;
                weight.weight1 = lowerBone == upperBone ? 0f : 0.5f;

                boneWeights[leftIndex] = weight;
                boneWeights[rightIndex] = weight;
            }

            List<int> triangles = new List<int>();
            for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
            {
                int bottomLeft = segmentIndex * 2;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + 2;
                int topRight = bottomLeft + 3;

                triangles.Add(bottomLeft); triangles.Add(topLeft); triangles.Add(bottomRight);
                triangles.Add(bottomRight); triangles.Add(topLeft); triangles.Add(topRight);
            }

            Matrix4x4[] bindposes = new Matrix4x4[bones.Length];
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                bindposes[boneIndex] =
                    bones[boneIndex].worldToLocalMatrix * rootTransform.localToWorldMatrix;
            }

            Mesh mesh = new Mesh();
            mesh.name = "TentacleStrip";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.boneWeights = boneWeights;
            mesh.bindposes = bindposes;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A travelling wave: every bone rotates on Z, each lagging the one below it.
        /// </summary>
        /// <remarks>
        /// The phase offset is what makes it a wave rather than a windscreen wiper. It also means
        /// no two bones hold the same value on any frame, so a bake that collapsed the chain — one
        /// bone's matrix written for all of them, a common addressing slip — shows up as a rigid rod
        /// rather than as a slightly wrong curve.
        /// </remarks>
        private static AnimationClip BuildWaveClip()
        {
            AnimationClip clip = new AnimationClip();
            clip.name = "TentacleWave";
            clip.legacy = false;

            int sampleCount = Mathf.RoundToInt(WaveDurationSeconds * WaveSampleRate);
            string bonePath = string.Empty;

            for (int boneIndex = 0; boneIndex < SegmentCount; boneIndex++)
            {
                bonePath = boneIndex == 0 ? "Bone0" : bonePath + "/Bone" + boneIndex.ToString();

                AnimationCurve curveX = new AnimationCurve();
                AnimationCurve curveY = new AnimationCurve();
                AnimationCurve curveZ = new AnimationCurve();
                AnimationCurve curveW = new AnimationCurve();

                for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
                {
                    float time = (float)sampleIndex / WaveSampleRate;
                    float phase = time * Mathf.PI * 2f / WaveDurationSeconds - boneIndex * 0.55f;

                    // Amplitude grows toward the tip, which is how a real tentacle moves and what
                    // makes the far end unmistakably the far end.
                    float amplitudeDegrees = 4f + 9f * ((float)boneIndex / SegmentCount);
                    float angleDegrees = Mathf.Sin(phase) * amplitudeDegrees;

                    Quaternion rotation = Quaternion.Euler(0f, 0f, angleDegrees);
                    curveX.AddKey(time, rotation.x);
                    curveY.AddKey(time, rotation.y);
                    curveZ.AddKey(time, rotation.z);
                    curveW.AddKey(time, rotation.w);
                }

                AnimationUtility.SetEditorCurve(
                    clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localRotation.x"), curveX);
                AnimationUtility.SetEditorCurve(
                    clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localRotation.y"), curveY);
                AnimationUtility.SetEditorCurve(
                    clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localRotation.z"), curveZ);
                AnimationUtility.SetEditorCurve(
                    clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localRotation.w"), curveW);
            }

            return clip;
        }

        /// <summary>Frames the wave clip is baked at.</summary>
        public static float BakeSampleRate
        {
            get { return WaveSampleRate; }
        }
    }
}
