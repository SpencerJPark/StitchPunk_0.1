// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public struct VatBakeClip
    {
        /// <summary>The stable id of the <c>ClipAsset</c> this animation corresponds to.</summary>
        public ulong clipId;

        /// <summary>
        /// Stable id of the rig target this bake is scoped to, or 0 for the clip's untargeted range
        /// baked from <c>ClipAsset.vatSource</c>. A non-zero value bakes an additional frame block
        /// for the same <see cref="clipId"/>, occupying its own row range in the same texture.
        /// </summary>
        public uint targetId;

        /// <summary>The animation to sample, or null when the clip is authored-only.</summary>
        public AnimationClip animationClip;

        /// <summary>Authored bone tracks applied on top of <see cref="animationClip"/>, or null.</summary>
        public List<BoneTrack> boneTracks;

        /// <summary>
        /// Clip length in seconds, used when <see cref="animationClip"/> is null.
        /// </summary>
        public float durationSeconds;

        /// <summary>
        /// Poses baked per second of this clip's time, or 0 to fall back to
        /// <see cref="VatBakeInput.samplesPerSecond"/>.
        /// </summary>
        public float samplesPerSecond;

        /// <summary>
        /// Append a duplicate of frame 0 after the last frame, so the shader's two-row lerp never
        /// reads across the clip boundary at the loop seam.
        /// </summary>
        public bool loopSafe;
    }

    public struct VatBakeInput
    {
        /// <summary>The skinned renderer to sample. Its root drives the animation.</summary>
        public SkinnedMeshRenderer skinnedMeshRenderer;

        /// <summary>Bone matrices or vertex positions.</summary>
        public VatFlavor flavor;

        /// <summary>
        /// Frames sampled per second of clip time, for clips that do not carry their own
        /// <see cref="VatBakeClip.samplesPerSecond"/>. Every clip baked from a <c>ClipAsset</c>
        /// does, so this is the rate for imported clips reached from a script or a test.
        /// </summary>
        public float samplesPerSecond;

        /// <summary>Clips to bake, laid end to end into one texture.</summary>
        public List<VatBakeClip> clips;

        /// <summary>
        /// Bake to RGBAFloat instead of RGBAHalf. Doubles the memory and removes the precision
        /// cliff for rigs much larger than a couple of metres.
        /// </summary>
        public bool useFullPrecision;

        // Sampled in a second pass over each clip rather than woven into the texture pass, so a
        // socket that fails to resolve cannot corrupt a texture.
        public List<VatBakeSocket> sockets;
    }

    public struct VatBakeSocket
    {
        /// <summary>Stable id of the socket row on the rig.</summary>
        public uint socketId;

        /// <summary>
        /// Name of the bone to follow, matched against the source hierarchy. Names are the only
        /// handle an imported rig offers — this package cannot assign ids inside a hierarchy it
        /// does not own — so an unmatched name is reported rather than silently baked as a socket
        /// pinned to the origin.
        /// </summary>
        public string boneName;
    }

    // Failures are reported through failed/message rather than thrown, so a bake can be driven
    // from a batch script over a content library without a zero-bone mesh aborting the run.
    public struct VatBakeResult
    {
        public bool failed;
        public string message;

        public Texture2D boneOrPositionTexture;
        public Texture2D normalTexture;

        public int textureWidth;
        public int rowsPerFrame;
        public int boneCount;
        public int vertexCount;
        public List<VatClipRange> clipRanges;

        /// <summary>Captured bone-socket motion; empty when no sockets were requested.</summary>
        public List<VatSocketTrack> socketTracks;

        /// <summary>
        /// Sockets whose bone name matched nothing in the source hierarchy. Non-fatal — the
        /// textures are still valid — but every listed socket will sit at the actor origin, so the
        /// caller is expected to surface these rather than treat an empty bake as success.
        /// </summary>
        public List<string> unresolvedSocketBones;

        /// <summary>
        /// Authored bone-track names that matched nothing in the source hierarchy. Non-fatal for
        /// the same reason as <see cref="unresolvedSocketBones"/>, but every listed bone stayed at
        /// rest, which presents as an animation that simply does not play rather than as an error.
        /// </summary>
        public List<string> unresolvedBoneTrackNames;

        /// <summary>Hash of every input that affects the output; changes when any of them does.</summary>
        public ulong sourceHash;
    }

    /// <summary>
    /// Bakes skinned animation into textures the GPU can play back without a skeleton. Frame
    /// <c>f</c> occupies <c>rowsPerFrame</c> rows from <c>f * rowsPerFrame</c>; element <c>e</c>
    /// sits at column <c>e % width</c>, row offset <c>e / width</c> — the layout <c>ToolkitVat.hlsl</c> expects.
    /// </summary>
    public static class VatTextureBaker
    {
        // Bone flavour writes a 3x4 matrix, one row per texture row.
        private const int BoneRowsPerFrame = 3;

        // Vertex flavour writes one position per element.
        private const int VertexRowsPerFrame = 1;

        // Power of two: keeps addressing exact in the shader's fmod/floor.
        private const int MaxTextureWidth = 1024;

        public static bool Bake(VatBakeInput input, out VatBakeResult result)
        {
            result = new VatBakeResult();
            result.clipRanges = new List<VatClipRange>();

            if (!Validate(input, ref result))
            {
                return false;
            }

            SkinnedMeshRenderer renderer = input.skinnedMeshRenderer;
            Mesh sharedMesh = renderer.sharedMesh;
            Transform rootTransform = renderer.transform.root;

            bool isBoneFlavor = input.flavor == VatFlavor.BoneMatrix;
            int elementCount = isBoneFlavor ? renderer.bones.Length : sharedMesh.vertexCount;

            result.rowsPerFrame = isBoneFlavor ? BoneRowsPerFrame : VertexRowsPerFrame;
            result.textureWidth = Mathf.Min(MaxTextureWidth, Mathf.NextPowerOfTwo(Mathf.Max(1, elementCount)));
            result.boneCount = isBoneFlavor ? elementCount : 0;
            result.vertexCount = isBoneFlavor ? 0 : elementCount;

            // Rows a single element block spans when the element count exceeds the width.
            int wrapRows = Mathf.CeilToInt((float)elementCount / result.textureWidth);
            int rowsPerFrameTotal = result.rowsPerFrame * wrapRows;

            List<Matrix4x4[]> framesOfMatrices = new List<Matrix4x4[]>();
            List<Vector3[]> framesOfPositions = new List<Vector3[]>();
            List<Vector3[]> framesOfNormals = new List<Vector3[]>();

            // Sampling happens inside AnimationMode, not through AnimationClip.SampleAnimation.
            // That method only drives LEGACY clips; against an ordinary imported clip it logs a
            // warning and poses nothing, so every frame would sample the rest pose and the bake
            // would produce a texture full of identical, entirely valid-looking matrices.
            result.socketTracks = new List<VatSocketTrack>();
            result.unresolvedSocketBones = new List<string>();
            List<Transform> socketBones = ResolveSocketBones(
                input.sockets, rootTransform, result.unresolvedSocketBones);

            // Bound once for the whole bake: the hierarchy does not change between clips, and a
            // tree walk per bone per frame is how a bake of a real rig becomes unusable.
            BoneTrackPoser bonePoser = new BoneTrackPoser();
            bonePoser.Bind(rootTransform);
            result.unresolvedBoneTrackNames = bonePoser.UnresolvedBoneNames;

            // AnimationMode's own revert cannot be relied on here: measured 2026-09-08, a bake left
            // every bone of the source rig at the last sampled pose even with sampling correctly
            // wrapped in Begin/EndSampling and AnimationMode stopped afterwards. Snapshotting the
            // local TRS ourselves makes the restore below independent of that.
            Transform[] posedTransforms = rootTransform.GetComponentsInChildren<Transform>(true);
            Vector3[] originalLocalPositions = new Vector3[posedTransforms.Length];
            Quaternion[] originalLocalRotations = new Quaternion[posedTransforms.Length];
            Vector3[] originalLocalScales = new Vector3[posedTransforms.Length];
            for (int transformIndex = 0; transformIndex < posedTransforms.Length; transformIndex++)
            {
                originalLocalPositions[transformIndex] = posedTransforms[transformIndex].localPosition;
                originalLocalRotations[transformIndex] = posedTransforms[transformIndex].localRotation;
                originalLocalScales[transformIndex] = posedTransforms[transformIndex].localScale;
            }

            UnityEditor.AnimationMode.StartAnimationMode();
            int globalFrame = 0;
            try
            {
                for (int clipIndex = 0; clipIndex < input.clips.Count; clipIndex++)
                {
                    VatBakeClip bakeClip = input.clips[clipIndex];
                    int frameCount = SampleClip(
                        bakeClip, input, rootTransform, renderer, sharedMesh, isBoneFlavor,
                        framesOfMatrices, framesOfPositions, framesOfNormals, bonePoser);

                    SampleSocketsForClip(
                        bakeClip, input, rootTransform, socketBones, result.socketTracks, bonePoser);

                    result.clipRanges.Add(new VatClipRange
                    {
                        clipId = bakeClip.clipId,
                        targetId = bakeClip.targetId,
                        frameStart = globalFrame,
                        frameCount = frameCount,
                        fps = ResolveSampleRate(bakeClip, input)
                    });
                    globalFrame += frameCount;
                }
            }
            finally
            {
                // Order matters. Authored tracks were written straight onto Transforms, which
                // AnimationMode knows nothing about and will not undo — so the manual restore runs
                // first, then AnimationMode reverts what it posed. Reversing this would leave the
                // user's rig stuck in the last sampled pose, a destructive edit to their scene as a
                // side effect of what looks like a read-only operation.
                bonePoser.RestoreOriginalPose();
                UnityEditor.AnimationMode.StopAnimationMode();

                // Last, and authoritative: whatever the two restores above did or failed to do, the
                // rig ends the bake exactly as the user left it. Baking is a read of their scene.
                for (int transformIndex = 0; transformIndex < posedTransforms.Length; transformIndex++)
                {
                    Transform posedTransform = posedTransforms[transformIndex];
                    if (posedTransform == null)
                    {
                        continue;
                    }
                    posedTransform.localPosition = originalLocalPositions[transformIndex];
                    posedTransform.localRotation = originalLocalRotations[transformIndex];
                    posedTransform.localScale = originalLocalScales[transformIndex];
                }
            }

            int totalFrames = globalFrame;
            int textureHeight = Mathf.Max(1, totalFrames * rowsPerFrameTotal);

            TextureFormat format = input.useFullPrecision ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;

            result.boneOrPositionTexture = isBoneFlavor
                ? WriteBoneTexture(framesOfMatrices, result.textureWidth, textureHeight, elementCount, format)
                : WriteVectorTexture(framesOfPositions, result.textureWidth, textureHeight, elementCount, format);

            if (!isBoneFlavor)
            {
                result.normalTexture = WriteVectorTexture(
                    framesOfNormals, result.textureWidth, textureHeight, elementCount, format);
            }

            result.sourceHash = ComputeSourceHash(input, elementCount, totalFrames);
            result.message = "Baked " + totalFrames.ToString() + " frames of "
                + elementCount.ToString() + (isBoneFlavor ? " bones" : " vertices")
                + " into " + result.textureWidth.ToString() + "x" + textureHeight.ToString() + ".";
            return true;
        }

        // -------------------------------------------------------------------------------------

        private static bool Validate(VatBakeInput input, ref VatBakeResult result)
        {
            if (input.skinnedMeshRenderer == null)
            {
                return Fail(ref result, "No SkinnedMeshRenderer supplied.");
            }
            if (input.skinnedMeshRenderer.sharedMesh == null)
            {
                return Fail(ref result, "The SkinnedMeshRenderer has no mesh.");
            }
            if (input.clips == null || input.clips.Count == 0)
            {
                return Fail(ref result, "No clips supplied to bake.");
            }
            if (input.samplesPerSecond <= 0f)
            {
                return Fail(ref result, "samplesPerSecond must be positive.");
            }

            if (input.flavor == VatFlavor.BoneMatrix && input.skinnedMeshRenderer.bones.Length == 0)
            {
                // Soft failure, not an exception: vertex flavour can still bake this mesh, and a
                // batch job over a library should skip the model rather than abort the run.
                return Fail(
                    ref result,
                    "Bone-flavour bake needs bones and this mesh has none. Use VertexPosition "
                    + "flavour for unskinned or blendshape-driven meshes.");
            }
            return true;
        }

        private static void PoseHierarchy(
            VatBakeClip bakeClip,
            Transform rootTransform,
            BoneTrackPoser bonePoser,
            float timeSeconds,
            float clipLengthSeconds)
        {
            // Imported clip poses first, authored tracks second, so authored keys override
            // imported motion on the bones they name — never the reverse.
            if (bakeClip.animationClip != null)
            {
                UnityEditor.AnimationMode.BeginSampling();
                UnityEditor.AnimationMode.SampleAnimationClip(
                    rootTransform.gameObject, bakeClip.animationClip, timeSeconds);
                UnityEditor.AnimationMode.EndSampling();
            }

            if (bakeClip.boneTracks == null || bakeClip.boneTracks.Count == 0)
            {
                return;
            }

            float normalizedTime = clipLengthSeconds > 1e-6f
                ? Mathf.Clamp01(timeSeconds / clipLengthSeconds)
                : 0f;
            bonePoser.ApplyTracks(bakeClip.boneTracks, normalizedTime);
        }

        private static bool Fail(ref VatBakeResult result, string message)
        {
            result.failed = true;
            result.message = message;
            return false;
        }

        // Resolved once for the whole bake, not per clip — the hierarchy does not change between
        // clips. Unresolved names produce a null slot and a reported name, never a substitute bone.
        private static List<Transform> ResolveSocketBones(
            List<VatBakeSocket> sockets,
            Transform rootTransform,
            List<string> unresolvedBoneNames)
        {
            List<Transform> resolvedBones = new List<Transform>();
            if (sockets == null)
            {
                return resolvedBones;
            }

            Transform[] hierarchy = rootTransform.GetComponentsInChildren<Transform>(true);
            for (int socketIndex = 0; socketIndex < sockets.Count; socketIndex++)
            {
                string boneName = sockets[socketIndex].boneName;
                Transform matchedBone = null;
                if (!string.IsNullOrEmpty(boneName))
                {
                    for (int boneIndex = 0; boneIndex < hierarchy.Length; boneIndex++)
                    {
                        if (hierarchy[boneIndex].name == boneName)
                        {
                            matchedBone = hierarchy[boneIndex];
                            break;
                        }
                    }
                }
                if (matchedBone == null)
                {
                    unresolvedBoneNames.Add(string.IsNullOrEmpty(boneName) ? "<unnamed>" : boneName);
                }
                resolvedBones.Add(matchedBone);
            }
            return resolvedBones;
        }

        // Must run inside an active AnimationMode, and its sample times must match SampleClip's
        // exactly — sample n of a socket track and frame n of the texture must describe the same instant.
        private static void SampleSocketsForClip(
            VatBakeClip bakeClip,
            VatBakeInput input,
            Transform rootTransform,
            List<Transform> socketBones,
            List<VatSocketTrack> socketTracks,
            BoneTrackPoser bonePoser)
        {
            if (input.sockets == null || input.sockets.Count == 0)
            {
                return;
            }

            AnimationClip animationClip = bakeClip.animationClip;
            float clipLengthSeconds = animationClip != null ? animationClip.length : bakeClip.durationSeconds;
            float sampleRate = ResolveSampleRate(bakeClip, input);
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(clipLengthSeconds * sampleRate));

            List<VatSocketTrack> tracksForThisClip = new List<VatSocketTrack>();
            for (int socketIndex = 0; socketIndex < input.sockets.Count; socketIndex++)
            {
                if (socketBones[socketIndex] == null)
                {
                    tracksForThisClip.Add(null);
                    continue;
                }
                tracksForThisClip.Add(new VatSocketTrack
                {
                    clipId = bakeClip.clipId,
                    socketId = input.sockets[socketIndex].socketId,
                    fps = sampleRate
                });
            }

            Matrix4x4 worldToRoot = rootTransform.worldToLocalMatrix;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = sampleCount == 1
                    ? 0f
                    : clipLengthSeconds * sampleIndex / sampleCount;

                PoseHierarchy(bakeClip, rootTransform, bonePoser, time, clipLengthSeconds);

                AppendSocketSamples(tracksForThisClip, socketBones, worldToRoot);
            }

            // Mirrors the texture's loop-safe row, so an attachment interpolating toward the last
            // sample lands on the loop start rather than whipping back through the whole clip.
            if (bakeClip.loopSafe)
            {
                PoseHierarchy(bakeClip, rootTransform, bonePoser, 0f, clipLengthSeconds);
                AppendSocketSamples(tracksForThisClip, socketBones, worldToRoot);
            }

            for (int socketIndex = 0; socketIndex < tracksForThisClip.Count; socketIndex++)
            {
                if (tracksForThisClip[socketIndex] != null)
                {
                    socketTracks.Add(tracksForThisClip[socketIndex]);
                }
            }
        }

        private static void AppendSocketSamples(
            List<VatSocketTrack> tracksForThisClip,
            List<Transform> socketBones,
            Matrix4x4 worldToRoot)
        {
            for (int socketIndex = 0; socketIndex < tracksForThisClip.Count; socketIndex++)
            {
                VatSocketTrack track = tracksForThisClip[socketIndex];
                if (track == null)
                {
                    continue;
                }
                Matrix4x4 boneToRoot = worldToRoot * socketBones[socketIndex].localToWorldMatrix;
                track.positions.Add(boneToRoot.GetPosition());
                track.rotations.Add(boneToRoot.rotation);
            }
        }

        /// <summary>
        /// Samples one clip at the bake rate, appending a frame's worth of data per sample.
        /// </summary>
        private static int SampleClip(
            VatBakeClip bakeClip,
            VatBakeInput input,
            Transform rootTransform,
            SkinnedMeshRenderer renderer,
            Mesh sharedMesh,
            bool isBoneFlavor,
            List<Matrix4x4[]> framesOfMatrices,
            List<Vector3[]> framesOfPositions,
            List<Vector3[]> framesOfNormals,
            BoneTrackPoser bonePoser)
        {
            AnimationClip animationClip = bakeClip.animationClip;
            float clipLengthSeconds = animationClip != null ? animationClip.length : bakeClip.durationSeconds;
            int sampleCount = Mathf.Max(
                1, Mathf.RoundToInt(clipLengthSeconds * ResolveSampleRate(bakeClip, input)));

            Matrix4x4 worldToRoot = rootTransform.worldToLocalMatrix;
            Matrix4x4[] bindposes = sharedMesh.bindposes;
            Transform[] bones = renderer.bones;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = sampleCount == 1
                    ? 0f
                    : clipLengthSeconds * sampleIndex / sampleCount;

                PoseHierarchy(bakeClip, rootTransform, bonePoser, time, clipLengthSeconds);

                if (isBoneFlavor)
                {
                    Matrix4x4[] frameMatrices = new Matrix4x4[bones.Length];
                    for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                    {
                        // Root-relative so the bake does not depend on where the rig sat in the
                        // scene, and so magnitudes stay small enough for half precision.
                        frameMatrices[boneIndex] =
                            worldToRoot * bones[boneIndex].localToWorldMatrix * bindposes[boneIndex];
                    }
                    framesOfMatrices.Add(frameMatrices);
                }
                else
                {
                    Mesh bakedMesh = new Mesh();
                    renderer.BakeMesh(bakedMesh, true);
                    framesOfPositions.Add(bakedMesh.vertices);
                    framesOfNormals.Add(bakedMesh.normals);
                    Object.DestroyImmediate(bakedMesh);
                }
            }

            // The duplicated frame is what lets the shader lerp floor→floor+1 at the last frame
            // without reading the next clip's first row.
            if (bakeClip.loopSafe)
            {
                if (isBoneFlavor)
                {
                    framesOfMatrices.Add(framesOfMatrices[framesOfMatrices.Count - sampleCount]);
                }
                else
                {
                    framesOfPositions.Add(framesOfPositions[framesOfPositions.Count - sampleCount]);
                    framesOfNormals.Add(framesOfNormals[framesOfNormals.Count - sampleCount]);
                }
                sampleCount++;
            }
            return sampleCount;
        }

        private static Texture2D WriteBoneTexture(
            List<Matrix4x4[]> frames, int width, int height, int elementCount, TextureFormat format)
        {
            Texture2D texture = CreateTexture(width, height, format);
            Color[] pixels = new Color[width * height];

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                Matrix4x4[] frameMatrices = frames[frameIndex];
                for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
                {
                    Matrix4x4 boneMatrix = frameMatrices[elementIndex];
                    int column = elementIndex % width;
                    int wrapRow = elementIndex / width;

                    for (int matrixRow = 0; matrixRow < BoneRowsPerFrame; matrixRow++)
                    {
                        int row = frameIndex * BoneRowsPerFrame + matrixRow + wrapRow;
                        if (row >= height)
                        {
                            continue;
                        }
                        pixels[row * width + column] = new Color(
                            boneMatrix[matrixRow, 0],
                            boneMatrix[matrixRow, 1],
                            boneMatrix[matrixRow, 2],
                            boneMatrix[matrixRow, 3]);
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D WriteVectorTexture(
            List<Vector3[]> frames, int width, int height, int elementCount, TextureFormat format)
        {
            Texture2D texture = CreateTexture(width, height, format);
            Color[] pixels = new Color[width * height];

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                Vector3[] frameVectors = frames[frameIndex];
                for (int elementIndex = 0; elementIndex < elementCount && elementIndex < frameVectors.Length; elementIndex++)
                {
                    int column = elementIndex % width;
                    int row = frameIndex * VertexRowsPerFrame + elementIndex / width;
                    if (row >= height)
                    {
                        continue;
                    }
                    Vector3 value = frameVectors[elementIndex];
                    pixels[row * width + column] = new Color(value.x, value.y, value.z, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // Every site that needs the rate — row count, socket samples, the range's fps, the hash —
        // goes through here, so sampling and labelling can never disagree on speed.
        private static float ResolveSampleRate(VatBakeClip bakeClip, VatBakeInput input)
        {
            return bakeClip.samplesPerSecond > 0f ? bakeClip.samplesPerSecond : input.samplesPerSecond;
        }

        /// <summary>
        /// Point filtering and clamped wrapping are not stylistic. A bilinear sampler would blend
        /// neighbouring bones or vertices — an average of two unrelated joints — and repeat wrapping
        /// would make an off-by-one row read the far side of the texture instead of clamping.
        /// </summary>
        private static Texture2D CreateTexture(int width, int height, TextureFormat format)
        {
            Texture2D texture = new Texture2D(width, height, format, false, true);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            return texture;
        }

        /// <summary>
        /// Folds every input that affects the output into one value, so a stale texture set is
        /// detectable without re-baking to compare.
        /// </summary>
        private static ulong ComputeSourceHash(VatBakeInput input, int elementCount, int totalFrames)
        {
            ulong hash = 1469598103934665603UL;
            hash = FoldHash(hash, (ulong)input.flavor);
            hash = FoldHash(hash, (ulong)Mathf.RoundToInt(input.samplesPerSecond * 1000f));
            hash = FoldHash(hash, (ulong)elementCount);
            hash = FoldHash(hash, (ulong)totalFrames);
            hash = FoldHash(hash, input.useFullPrecision ? 1UL : 0UL);

            for (int clipIndex = 0; clipIndex < input.clips.Count; clipIndex++)
            {
                VatBakeClip bakeClip = input.clips[clipIndex];
                hash = FoldHash(hash, bakeClip.clipId);
                hash = FoldHash(hash, (ulong)bakeClip.targetId);
                hash = FoldHash(hash, bakeClip.loopSafe ? 1UL : 0UL);
                hash = FoldHash(
                    hash, (ulong)Mathf.RoundToInt(ResolveSampleRate(bakeClip, input) * 1000f));
                if (bakeClip.animationClip != null)
                {
                    hash = FoldHash(hash, (ulong)Mathf.RoundToInt(bakeClip.animationClip.length * 1000f));
                    hash = FoldHash(hash, (ulong)bakeClip.animationClip.name.GetHashCode());
                }
            }
            return hash;
        }

        private static ulong FoldHash(ulong hash, ulong value)
        {
            return (hash ^ value) * 1099511628211UL;
        }
    }
}
