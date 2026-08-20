// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One clip to bake into a VAT texture.
    /// </summary>
    public struct VatBakeClip
    {
        /// <summary>The stable id of the <c>ClipAsset</c> this animation corresponds to.</summary>
        public ulong clipId;

        /// <summary>
        /// Stable id of the rig target this bake is scoped to, or 0 for the clip's untargeted range
        /// baked from <c>ClipAsset.vatSource</c> (C10). A non-zero value bakes an additional frame
        /// block for the same <see cref="clipId"/>, occupying its own row range in the same texture —
        /// see <see cref="VatBakeResult.clipRanges"/>.
        /// </summary>
        public uint targetId;

        /// <summary>The animation to sample, or null when the clip is authored-only.</summary>
        public AnimationClip animationClip;

        /// <summary>
        /// Authored bone tracks applied on top of <see cref="animationClip"/> (amendment A42), or
        /// null. A bake clip may carry an imported clip, authored tracks, or both.
        /// </summary>
        public List<BoneTrack> boneTracks;

        /// <summary>
        /// Clip length in seconds, used when <see cref="animationClip"/> is null. An authored-only
        /// clip has no imported asset to take a length from, so the <c>ClipAsset</c>'s own duration
        /// is the only source of truth for how many frames to bake.
        /// </summary>
        public float durationSeconds;

        /// <summary>
        /// Append a duplicate of frame 0 after the last frame, so the shader's two-row lerp never
        /// reads across the clip boundary at the loop seam.
        /// </summary>
        public bool loopSafe;
    }

    /// <summary>
    /// Everything <see cref="VatTextureBaker"/> needs. A plain struct so the baker is callable
    /// headlessly — from a window, a test, or a build script — rather than only from UI.
    /// </summary>
    public struct VatBakeInput
    {
        /// <summary>The skinned renderer to sample. Its root drives the animation.</summary>
        public SkinnedMeshRenderer skinnedMeshRenderer;

        /// <summary>Bone matrices or vertex positions.</summary>
        public VatFlavor flavor;

        /// <summary>Frames sampled per second of clip time.</summary>
        public float samplesPerSecond;

        /// <summary>Clips to bake, laid end to end into one texture.</summary>
        public List<VatBakeClip> clips;

        /// <summary>
        /// Bake to RGBAFloat instead of RGBAHalf. Doubles the memory and removes the precision
        /// cliff §12 R2 describes for rigs much larger than a couple of metres.
        /// </summary>
        public bool useFullPrecision;

        /// <summary>
        /// Bone sockets to capture alongside the textures, or null for none.
        /// </summary>
        /// <remarks>
        /// Sampled in a second pass over each clip rather than woven into the texture pass. The
        /// duplicated sampling costs bake time nobody waits on, and buys complete isolation: a
        /// socket that fails to resolve cannot corrupt a texture, and the texture path is unchanged
        /// whether sockets are requested or not.
        /// </remarks>
        public List<VatBakeSocket> sockets;
    }

    /// <summary>One bone socket to capture during a bake.</summary>
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

    /// <summary>
    /// What the bake produced, or why it produced nothing.
    /// </summary>
    /// <remarks>
    /// Failures are reported through <see cref="failed"/> and <see cref="message"/> rather than
    /// thrown (§8 M2: "never throws past the API"). A baker that throws cannot be driven from a
    /// batch script over a content library, which is exactly when a zero-bone mesh turns up.
    /// </remarks>
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
        /// Authored bone-track names that matched nothing in the source hierarchy (amendment A42).
        /// Non-fatal for the same reason as <see cref="unresolvedSocketBones"/> — the textures are
        /// valid — but every listed bone stayed at rest, which presents as an animation that simply
        /// does not play rather than as an error.
        /// </summary>
        public List<string> unresolvedBoneTrackNames;

        /// <summary>Hash of every input that affects the output; changes when any of them does.</summary>
        public ulong sourceHash;
    }

    /// <summary>
    /// Bakes skinned animation into textures the GPU can play back without a skeleton
    /// (architecture section 4.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Bone flavour writes object-space skinning matrices</strong> — the same
    /// <c>boneToWorld × bindpose</c> product a CPU skinner would use, expressed relative to the
    /// renderer's root so the result is independent of where the rig sat in the scene when it was
    /// baked. Keeping magnitudes small is also what makes half precision viable (§12 R2).
    /// </para>
    /// <para>
    /// <strong>The layout is the contract with <c>ToolkitVat.hlsl</c>.</strong> Frame <c>f</c>
    /// occupies <c>rowsPerFrame</c> consecutive rows from <c>f * rowsPerFrame</c>; element
    /// <c>e</c> sits at column <c>e % width</c> on row offset <c>e / width</c>. Changing either
    /// side without the other produces a mesh that renders as noise, so the two are documented
    /// against each other rather than independently.
    /// </para>
    /// </remarks>
    public static class VatTextureBaker
    {
        /// <summary>Bone flavour writes a 3x4 matrix, one row per texture row.</summary>
        private const int BoneRowsPerFrame = 3;

        /// <summary>Vertex flavour writes one position per element.</summary>
        private const int VertexRowsPerFrame = 1;

        /// <summary>
        /// Texture width. Powers of two keep addressing exact in the shader's fmod/floor and stay
        /// friendly to every platform's texture rules.
        /// </summary>
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
                        fps = input.samplesPerSecond
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

        /// <summary>
        /// Rejects inputs that cannot produce a texture, with a message naming what to fix.
        /// </summary>
        /// <remarks>
        /// The zero-bone case is called out by §11.3 because it is the one a content pipeline hits
        /// by accident: a mesh exported without skinning looks identical in the project window and
        /// fails only here.
        /// </remarks>
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

        /// <summary>
        /// Poses the hierarchy for one sample, from whichever sources this clip carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The imported clip poses first, authored tracks second, so authored keys override
        /// imported motion on the bones they name (amendment A42). That order makes an authored
        /// track the more specific statement of intent, which is the case this feature exists for:
        /// an imported walk cycle with a hand-authored arm on top. The reverse would let the import
        /// silently erase deliberate hand-authoring.
        /// </para>
        /// <para>
        /// A clip with no imported source poses purely from authored tracks — <c>AnimationMode</c>
        /// is skipped entirely rather than called with null, which logs and poses nothing.
        /// </para>
        /// </remarks>
        private static void PoseHierarchy(
            VatBakeClip bakeClip,
            Transform rootTransform,
            BoneTrackPoser bonePoser,
            float timeSeconds,
            float clipLengthSeconds)
        {
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

        /// <summary>
        /// Finds each requested socket's bone in the source hierarchy, by name.
        /// </summary>
        /// <remarks>
        /// Resolved once for the whole bake rather than per clip: the hierarchy does not change
        /// between clips, and a per-clip search would repeat a full-tree walk hundreds of times.
        /// Unresolved names produce a null slot and a reported name — never a substitute bone,
        /// because a socket silently bound to the wrong bone is far worse than one that is
        /// obviously missing.
        /// </remarks>
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

        /// <summary>
        /// Captures every resolved socket's root-relative transform across one clip.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A second pass over the same clip, at the same rate and the same sample times as
        /// <see cref="SampleClip"/> — including the loop-safe duplicate — so sample <c>n</c> of a
        /// socket track and frame <c>n</c> of the texture describe the same instant. The two loops
        /// must agree on timing; sharing the arithmetic rather than the loop is what keeps the
        /// socket path from being able to break the texture path.
        /// </para>
        /// <para>
        /// Must be called inside an active <c>AnimationMode</c> — it poses the hierarchy exactly as
        /// the texture pass does.
        /// </para>
        /// </remarks>
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
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(clipLengthSeconds * input.samplesPerSecond));

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
                    fps = input.samplesPerSecond
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

            // The duplicated final frame mirrors the texture's loop-safe row (§4.7), so an
            // attachment interpolating toward the last sample lands on the loop start rather than
            // whipping back through the whole clip.
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
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(clipLengthSeconds * input.samplesPerSecond));

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
            // without reading the next clip's first row (§4.7).
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
        /// detectable (validation rule V08) without re-baking to compare.
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
