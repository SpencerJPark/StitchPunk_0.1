// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Builds a procedural tentacle — a tall strip with a bone chain down its length — as a
    /// reference subject for VAT baking, generated rather than imported so no binary test
    /// content ships with the package.
    /// </summary>
    public static class VatTentacleRigBuilder
    {
        private const int SegmentCount = 12;
        private const float SegmentLength = 0.25f;
        private const float TentacleWidth = 0.22f;
        private const int WaveSampleRate = 30;
        private const float WaveDurationSeconds = 2f;

        // Keys per bone lane. Enough that an eased sample reads as a sine, few enough that a lane in
        // the Clip Editor is something a person can grab and retime rather than a wall of keys.
        // (WaveKeysPerBone - 1) must divide the clip's frame count, or every key between the ends
        // lands mid-frame and the editor opens offering to quantize the sample it just generated.
        private const int WaveKeysPerBone = 11;

        /// <summary>
        /// Creates the rig in the current scene and returns its <see cref="SkinnedMeshRenderer"/>.
        /// </summary>
        public static SkinnedMeshRenderer CreateTentacle(string name, out List<BoneTrack> waveBoneTracks)
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
            renderer.sharedMaterial = BuildTentacleMaterial();

            waveBoneTracks = BuildWaveBoneTracks();
            return renderer;
        }

        // A renderer with no material draws magenta, which reads as a broken sample rather than a
        // sample with nothing assigned. Standard is the fallback for a project without URP.
        private static Material BuildTentacleMaterial()
        {
            Shader tentacleShader = Shader.Find("Universal Render Pipeline/Lit");
            if (tentacleShader == null)
            {
                tentacleShader = Shader.Find("Standard");
            }
            if (tentacleShader == null)
            {
                return null;
            }

            Material tentacleMaterial = new Material(tentacleShader);
            tentacleMaterial.name = "TentacleMaterial";
            Color tentacleColor = new Color(0.62f, 0.66f, 0.70f, 1f);
            if (tentacleMaterial.HasProperty("_BaseColor"))
            {
                tentacleMaterial.SetColor("_BaseColor", tentacleColor);
            }
            if (tentacleMaterial.HasProperty("_Color"))
            {
                tentacleMaterial.SetColor("_Color", tentacleColor);
            }
            return tentacleMaterial;
        }

        // A two-vertex-wide strip running up Y, skinned to the chain: each ring is weighted
        // between its two neighboring bones, linearly by how far along the segment it sits.
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

        // Authored as BoneTracks, not as an imported AnimationClip, so the sample's motion is visible
        // and editable in the Clip Editor's own timeline — bone tracks make a clip VAT-bound alone.
        private static List<BoneTrack> BuildWaveBoneTracks()
        {
            List<BoneTrack> boneTracks = new List<BoneTrack>(SegmentCount);
            for (int boneIndex = 0; boneIndex < SegmentCount; boneIndex++)
            {
                BoneTrack boneTrack = new BoneTrack();
                boneTrack.boneName = "Bone" + boneIndex.ToString();

                // The chain's rest offset, restated on every key: ApplyTracks assigns localPosition
                // outright rather than adding to the bind pose, so a key leaving it at zero would
                // collapse the whole tentacle onto its root.
                float3 restLocalPosition = boneIndex == 0
                    ? new float3(0f, 0f, 0f)
                    : new float3(0f, SegmentLength, 0f);

                for (int keyIndex = 0; keyIndex < WaveKeysPerBone; keyIndex++)
                {
                    float normalizedTime = (float)keyIndex / (WaveKeysPerBone - 1);
                    Quaternion rotation = Quaternion.Euler(0f, 0f, WaveAngleDegrees(boneIndex, normalizedTime));

                    BoneKey boneKey = new BoneKey();
                    boneKey.normalizedTime = normalizedTime;
                    boneKey.localPosition = restLocalPosition;
                    boneKey.localRotation = new quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
                    boneKey.localScale = new float3(1f, 1f, 1f);
                    boneKey.interpolation = Interpolation.EaseInOut;
                    boneTrack.keys.Add(boneKey);
                }

                boneTracks.Add(boneTrack);
            }
            return boneTracks;
        }

        // One travelling sine down the chain. The phase lag per bone is what makes it read as a wave
        // rather than every joint swinging together, and the amplitude grows toward the tip, which is
        // how a real tentacle moves and what makes the far end unmistakably the far end.
        private static float WaveAngleDegrees(int boneIndex, float normalizedTime)
        {
            float phase = normalizedTime * Mathf.PI * 2f - boneIndex * 0.55f;
            float amplitudeDegrees = 4f + 9f * ((float)boneIndex / SegmentCount);
            return Mathf.Sin(phase) * amplitudeDegrees;
        }

        /// <summary>Length of the generated wave, in seconds.</summary>
        public static float WaveDuration
        {
            get { return WaveDurationSeconds; }
        }

        /// <summary>Frames the wave clip is baked at.</summary>
        public static float BakeSampleRate
        {
            get { return WaveSampleRate; }
        }
    }
}
