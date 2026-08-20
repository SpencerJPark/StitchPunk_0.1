// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>VatTextureBaker</c> — the M2 VAT slice of architecture section 4.7, which shipped
    /// with no coverage at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every fixture builds a <strong>procedural</strong> skinned mesh and, where motion is needed,
    /// a procedural <see cref="AnimationClip"/>. That is the approach §9's C6 row asks for, and it
    /// is the only one available: an imported FBX cannot be committed to a package's test folder
    /// without becoming a binary fixture nobody can diff or regenerate.
    /// </para>
    /// <para>
    /// <strong>What these fixtures pin is the layout contract and the failure contract, not the
    /// pixels.</strong> The texture layout is the shared secret with <c>ToolkitVat.hlsl</c> —
    /// "frame <c>f</c> occupies <c>rowsPerFrame</c> rows from <c>f * rowsPerFrame</c>" — and §8 M2
    /// requires the baker to report failure rather than throw, because a baker that throws cannot be
    /// driven over a content library. Both are checkable exactly; neither had a test.
    /// </para>
    /// <para>
    /// The baker drives <see cref="AnimationMode"/>, so these are EditMode fixtures that create real
    /// GameObjects. Every one is destroyed in <see cref="TearDown"/>, and the baker's own
    /// <c>finally</c> restores the posed hierarchy — a bake is specified to be read-only with
    /// respect to the user's scene, which <see cref="Baking_RestoresTheHierarchyItPosed"/> pins.
    /// </para>
    /// </remarks>
    public sealed class VatTextureBakerTests
    {
        private const float Tolerance = 1e-3f;
        private const ulong WalkClipId = 0x0000000000001001UL;
        private const ulong IdleClipId = 0x0000000000002002UL;

        private readonly List<Object> spawnedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // AnimationMode is stopped by the baker's finally, but a fixture that fails partway
            // through construction may never have started it. Leaving it on would pose every later
            // fixture's hierarchy and is global editor state, so it is force-cleared here.
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            for (int objectIndex = 0; objectIndex < spawnedObjects.Count; objectIndex++)
            {
                if (spawnedObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(spawnedObjects[objectIndex]);
                }
            }
            spawnedObjects.Clear();
        }

        // -----------------------------------------------------------------------------------
        // The failure contract (§8 M2: "never throws past the API").
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: the baker throwing on a mesh exported without skinning — §11.3 names this as the
        /// case a content pipeline hits by accident, because such a mesh looks identical in the
        /// project window and fails only here.
        /// </summary>
        [Test]
        public void ABoneBakeOfAMeshWithNoBones_FailsSoftlyAndSaysWhy()
        {
            SkinnedMeshRenderer renderer = CreateBonelessRenderer();

            bool succeeded = VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult result);

            Assert.IsFalse(succeeded);
            Assert.IsTrue(result.failed);
            StringAssert.Contains(
                "VertexPosition",
                result.message,
                "The message must name the flavour that can bake this mesh, or a batch job has "
                + "nothing to act on.");
        }

        /// <summary>
        /// Catches: dereferencing a null renderer. A batch script over a library reaches this the
        /// first time a prefab is missing its renderer.
        /// </summary>
        [Test]
        public void ABakeWithNoRenderer_FailsSoftly()
        {
            bool succeeded = VatTextureBaker.Bake(
                BuildInput(null, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult result);

            Assert.IsFalse(succeeded);
            Assert.IsTrue(result.failed);
            Assert.IsNotEmpty(result.message);
        }

        /// <summary>
        /// Catches: dividing by a zero sample rate, which produces either an exception or a texture
        /// of zero frames depending on where it lands.
        /// </summary>
        [Test]
        public void ABakeWithANonPositiveSampleRate_FailsSoftly()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f));
            input.samplesPerSecond = 0f;

            bool succeeded = VatTextureBaker.Bake(input, out VatBakeResult result);

            Assert.IsFalse(succeeded);
            Assert.IsTrue(result.failed);
        }

        /// <summary>Catches: baking an empty clip list into a zero-height texture.</summary>
        [Test]
        public void ABakeWithNoClips_FailsSoftly()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix);
            input.clips = new List<VatBakeClip>();

            bool succeeded = VatTextureBaker.Bake(input, out VatBakeResult result);

            Assert.IsFalse(succeeded);
            Assert.IsTrue(result.failed);
        }

        // -----------------------------------------------------------------------------------
        // The layout contract with ToolkitVat.hlsl (§4.7).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: changing <c>rowsPerFrame</c> on either side of the CPU/GPU contract alone. A
        /// bone frame is a 3×4 matrix written one matrix row per texture row; the shader reads three
        /// rows and reconstructs it. Four, or one, renders the mesh as noise.
        /// </summary>
        [Test]
        public void ABoneBake_WritesThreeRowsPerFrame()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult result), result.message);

            Assert.AreEqual(3, result.rowsPerFrame);
            Assert.AreEqual(2, result.boneCount);
            Assert.AreEqual(0, result.vertexCount, "A bone bake reports no vertex count.");
        }

        /// <summary>
        /// Catches: the vertex flavour inheriting the bone flavour's row count. A vertex frame is one
        /// position per element and therefore one row.
        /// </summary>
        [Test]
        public void AVertexBake_WritesOneRowPerFrame_AndANormalTexture()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.VertexPosition, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult result), result.message);

            Assert.AreEqual(1, result.rowsPerFrame);
            Assert.AreEqual(0, result.boneCount, "A vertex bake reports no bone count.");
            Assert.Greater(result.vertexCount, 0);
            Assert.IsNotNull(
                result.normalTexture,
                "A vertex bake must emit normals; without them the mesh lights as though it never "
                + "deformed.");
        }

        /// <summary>
        /// Catches: a texture width that is not a power of two. §4.7 requires one so the shader's
        /// fmod/floor addressing stays exact.
        /// </summary>
        [Test]
        public void TheTextureWidth_IsAPowerOfTwoCoveringTheElementCount()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 5);

            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult result), result.message);

            Assert.AreEqual(8, result.textureWidth, "Five bones must round up to a width of eight.");
            Assert.AreEqual(
                result.textureWidth,
                result.boneOrPositionTexture.width,
                "The reported width and the texture's own width must agree, or every shader read "
                + "addresses the wrong column.");
        }

        // -----------------------------------------------------------------------------------
        // Frame ranges (§4.7, C10).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: overlapping or mis-ordered clip ranges. Clips are laid end to end into one
        /// texture, so a second clip whose <c>frameStart</c> does not begin where the first ended
        /// plays part of its neighbour.
        /// </summary>
        [Test]
        public void TwoClips_AreLaidEndToEndWithoutOverlap()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix);
            input.samplesPerSecond = 10f;
            input.clips = new List<VatBakeClip>
            {
                AuthoredOnlyClip(WalkClipId, 1f),
                AuthoredOnlyClip(IdleClipId, 0.5f)
            };

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);

            Assert.AreEqual(2, result.clipRanges.Count);
            Assert.AreEqual(WalkClipId, result.clipRanges[0].clipId);
            Assert.AreEqual(0, result.clipRanges[0].frameStart);
            Assert.AreEqual(10, result.clipRanges[0].frameCount, "One second at 10 fps.");

            Assert.AreEqual(IdleClipId, result.clipRanges[1].clipId);
            Assert.AreEqual(
                10,
                result.clipRanges[1].frameStart,
                "The second clip must start where the first ended.");
            Assert.AreEqual(5, result.clipRanges[1].frameCount, "Half a second at 10 fps.");
        }

        /// <summary>
        /// Catches: dropping the loop-safe duplicate frame. Without it the shader's floor→floor+1
        /// lerp reads the *next clip's* first row at the seam, so a looping clip flickers through a
        /// frame of an unrelated animation once per cycle.
        /// </summary>
        [Test]
        public void ALoopSafeClip_BakesOneExtraFrame()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix);
            input.samplesPerSecond = 10f;
            VatBakeClip loopSafeClip = AuthoredOnlyClip(WalkClipId, 1f);
            loopSafeClip.loopSafe = true;
            input.clips = new List<VatBakeClip> { loopSafeClip };

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);

            Assert.AreEqual(
                11,
                result.clipRanges[0].frameCount,
                "Ten sampled frames plus the duplicate of frame 0.");
        }

        /// <summary>
        /// Catches: a targeted range (C10) overwriting the untargeted one instead of occupying its
        /// own block. Both carry the same <c>clipId</c>, so a bake that keyed ranges by clip alone
        /// would silently keep only the last.
        /// </summary>
        [Test]
        public void ATargetedRange_GetsItsOwnBlockAlongsideTheUntargetedOne()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeClip untargeted = AuthoredOnlyClip(WalkClipId, 1f);
            VatBakeClip targeted = AuthoredOnlyClip(WalkClipId, 1f);
            targeted.targetId = 0x00000042u;

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix);
            input.samplesPerSecond = 10f;
            input.clips = new List<VatBakeClip> { untargeted, targeted };

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);

            Assert.AreEqual(2, result.clipRanges.Count, "Both blocks must survive.");
            Assert.AreEqual(0u, result.clipRanges[0].targetId);
            Assert.AreEqual(0x00000042u, result.clipRanges[1].targetId);
            Assert.AreNotEqual(
                result.clipRanges[0].frameStart,
                result.clipRanges[1].frameStart,
                "The targeted block must occupy its own rows, not alias the untargeted one.");
        }

        // -----------------------------------------------------------------------------------
        // Sockets and unresolved names (non-fatal reporting).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: treating an unmatched socket bone as success. The textures are still valid, so
        /// nothing fails — but every listed socket sits at the actor origin, which presents as an
        /// attachment silently glued to the character's feet.
        /// </summary>
        [Test]
        public void ASocketNamingAMissingBone_IsReportedWithoutFailingTheBake()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f));
            input.sockets = new List<VatBakeSocket>
            {
                new VatBakeSocket { socketId = 7, boneName = "NoSuchBone" }
            };

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);

            Assert.IsFalse(result.failed, "An unresolved socket is non-fatal; the textures are valid.");
            CollectionAssert.Contains(result.unresolvedSocketBones, "NoSuchBone");
        }

        /// <summary>
        /// Catches: reporting a bone that <em>does</em> exist as unresolved, which would send an
        /// author hunting for a naming bug that is not there.
        /// </summary>
        [Test]
        public void ASocketNamingARealBone_IsNotReportedAsUnresolved()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f));
            input.sockets = new List<VatBakeSocket>
            {
                new VatBakeSocket { socketId = 7, boneName = BoneName(0) }
            };

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);

            CollectionAssert.IsEmpty(result.unresolvedSocketBones);
            Assert.AreEqual(1, result.socketTracks.Count, "The resolved socket must produce a track.");
        }

        // -----------------------------------------------------------------------------------
        // Source hash (the input V08 compares against).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: a hash that ignores an input which changes the output. Validation rule V08 uses
        /// it to decide whether baked textures are stale, so a hash blind to the sample rate reports
        /// a re-bake as unnecessary and ships the old texture.
        /// </summary>
        [Test]
        public void TheSourceHash_ChangesWithTheSampleRate()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput slowInput = BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f));
            slowInput.samplesPerSecond = 10f;
            Assert.IsTrue(VatTextureBaker.Bake(slowInput, out VatBakeResult slowResult), slowResult.message);

            VatBakeInput fastInput = BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f));
            fastInput.samplesPerSecond = 30f;
            Assert.IsTrue(VatTextureBaker.Bake(fastInput, out VatBakeResult fastResult), fastResult.message);

            Assert.AreNotEqual(slowResult.sourceHash, fastResult.sourceHash);
        }

        /// <summary>
        /// Catches: a hash that varies between runs of identical input — a non-deterministic hash
        /// makes V08 report every bake as stale, which trains authors to ignore it.
        /// </summary>
        [Test]
        public void TheSourceHash_IsStableAcrossIdenticalBakes()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult firstResult), firstResult.message);
            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AuthoredOnlyClip(WalkClipId, 1f)),
                out VatBakeResult secondResult), secondResult.message);

            Assert.AreEqual(firstResult.sourceHash, secondResult.sourceHash);
        }

        // -----------------------------------------------------------------------------------
        // The bake is read-only with respect to the user's scene.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: leaving the rig stuck in the last sampled pose. The baker's <c>finally</c>
        /// restores the hierarchy specifically because a bake looks like a read-only operation, and
        /// silently re-posing the user's scene is a destructive edit they did not ask for.
        /// </summary>
        /// <remarks>
        /// Uses a clip that genuinely moves the bone. With an authored-only clip nothing poses the
        /// hierarchy at all, so "it was restored" would be true of a baker that had no restore in it.
        /// </remarks>
        [Test]
        public void Baking_RestoresTheHierarchyItPosed()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);
            Transform firstBone = renderer.bones[0];
            Vector3 poseBeforeBake = firstBone.localPosition;

            Assert.IsTrue(VatTextureBaker.Bake(
                BuildInput(renderer, VatFlavor.BoneMatrix, AnimatedClip(WalkClipId)),
                out VatBakeResult result), result.message);

            Assert.AreEqual(poseBeforeBake.x, firstBone.localPosition.x, Tolerance,
                "The rig is stuck in a sampled pose; a bake must not edit the user's scene.");
            Assert.AreEqual(poseBeforeBake.y, firstBone.localPosition.y, Tolerance);
            Assert.AreEqual(poseBeforeBake.z, firstBone.localPosition.z, Tolerance);
            Assert.IsFalse(
                AnimationMode.InAnimationMode(),
                "AnimationMode is global editor state and must not outlive the bake.");
        }

        // -----------------------------------------------------------------------------------
        // The sampling loop actually samples.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// <strong>The failure the baker's own source warns about</strong>: sampling through
        /// <c>AnimationClip.SampleAnimation</c> (which drives only legacy clips) poses nothing, so
        /// every frame captures the rest pose and the bake yields "a texture full of identical,
        /// entirely valid-looking matrices". Every other fixture in this file passes against that
        /// bug — the dimensions, the ranges, the hash and the failure paths are all still correct.
        /// This one does not.
        /// </summary>
        [Test]
        public void AnAnimatedClip_WritesDifferentMatricesOnDifferentFrames()
        {
            SkinnedMeshRenderer renderer = CreateSkinnedRenderer(boneCount: 2);

            VatBakeInput input = BuildInput(renderer, VatFlavor.BoneMatrix, AnimatedClip(WalkClipId));
            input.samplesPerSecond = 10f;
            input.useFullPrecision = true;

            Assert.IsTrue(VatTextureBaker.Bake(input, out VatBakeResult result), result.message);
            Assert.Greater(result.clipRanges[0].frameCount, 2, "Guard: several frames must be baked.");

            // Row 0 is frame 0's first matrix row; each frame occupies rowsPerFrame rows.
            Color firstFrameRow = result.boneOrPositionTexture.GetPixel(0, 0);
            Color lastFrameRow = result.boneOrPositionTexture.GetPixel(
                0, (result.clipRanges[0].frameCount - 1) * result.rowsPerFrame);

            Assert.AreNotEqual(
                firstFrameRow.a,
                lastFrameRow.a,
                "Every frame holds the same matrix, so the clip was never sampled — the texture is "
                + "the rest pose repeated.");
        }

        // -----------------------------------------------------------------------------------
        // Fixture construction
        // -----------------------------------------------------------------------------------

        private static string BoneName(int boneIndex)
        {
            return "Bone" + boneIndex.ToString();
        }

        /// <summary>An authored-only bake clip: no imported animation, so its length is its duration.</summary>
        private static VatBakeClip AuthoredOnlyClip(ulong clipId, float durationSeconds)
        {
            return new VatBakeClip
            {
                clipId = clipId,
                targetId = 0u,
                animationClip = null,
                boneTracks = null,
                durationSeconds = durationSeconds,
                loopSafe = false
            };
        }

        /// <summary>
        /// A bake clip carrying a real <see cref="AnimationClip"/> that slides <c>Bone0</c> along X
        /// over one second, so consecutive frames differ.
        /// </summary>
        /// <remarks>
        /// Built with <see cref="AnimationUtility.SetEditorCurve"/> against the bone's path relative
        /// to the rig root, because that root is the GameObject <c>AnimationMode.SampleAnimationClip</c>
        /// is handed. A curve authored against any other path binds to nothing and poses nothing —
        /// which is indistinguishable, from the outside, from the sampling bug this clip exists to
        /// expose.
        /// </remarks>
        private VatBakeClip AnimatedClip(ulong clipId)
        {
            AnimationClip animationClip = new AnimationClip { name = "VatBakeMotion", legacy = false };
            spawnedObjects.Add(animationClip);

            AnimationUtility.SetEditorCurve(
                animationClip,
                EditorCurveBinding.FloatCurve(BoneName(0), typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, 1f, 5f));

            return new VatBakeClip
            {
                clipId = clipId,
                targetId = 0u,
                animationClip = animationClip,
                boneTracks = null,
                durationSeconds = 0f,
                loopSafe = false
            };
        }

        private static VatBakeInput BuildInput(
            SkinnedMeshRenderer renderer, VatFlavor flavor, params VatBakeClip[] clips)
        {
            return new VatBakeInput
            {
                skinnedMeshRenderer = renderer,
                flavor = flavor,
                samplesPerSecond = 10f,
                clips = new List<VatBakeClip>(clips),
                useFullPrecision = false,
                sockets = null
            };
        }

        /// <summary>
        /// A minimal skinned quad bound to <paramref name="boneCount"/> bones in a chain under a
        /// root, each vertex fully weighted to one bone.
        /// </summary>
        /// <remarks>
        /// Bones are offset along X rather than stacked at the origin: a bake that lost the
        /// per-bone transform would still produce plausible matrices from coincident bones, and the
        /// offsets are what make a wrong one visible as a wrong number.
        /// </remarks>
        private SkinnedMeshRenderer CreateSkinnedRenderer(int boneCount)
        {
            GameObject rootObject = new GameObject("VatBakeRig");
            spawnedObjects.Add(rootObject);

            Transform[] bones = new Transform[boneCount];
            Matrix4x4[] bindposes = new Matrix4x4[boneCount];
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                GameObject boneObject = new GameObject(BoneName(boneIndex));
                boneObject.transform.SetParent(rootObject.transform, false);
                boneObject.transform.localPosition = new Vector3(boneIndex, 0f, 0f);
                bones[boneIndex] = boneObject.transform;
                bindposes[boneIndex] =
                    boneObject.transform.worldToLocalMatrix * rootObject.transform.localToWorldMatrix;
            }

            Mesh mesh = new Mesh { name = "VatBakeMesh" };
            spawnedObjects.Add(mesh);

            int vertexCount = boneCount * 2;
            Vector3[] vertices = new Vector3[vertexCount];
            BoneWeight[] boneWeights = new BoneWeight[vertexCount];
            for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                vertices[boneIndex * 2] = new Vector3(boneIndex, 0f, 0f);
                vertices[boneIndex * 2 + 1] = new Vector3(boneIndex, 1f, 0f);
                boneWeights[boneIndex * 2] = new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
                boneWeights[boneIndex * 2 + 1] = new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
            }

            int[] triangles = new int[Mathf.Max(0, (boneCount - 1) * 6)];
            for (int quadIndex = 0; quadIndex < boneCount - 1; quadIndex++)
            {
                int baseVertex = quadIndex * 2;
                int baseTriangle = quadIndex * 6;
                triangles[baseTriangle] = baseVertex;
                triangles[baseTriangle + 1] = baseVertex + 1;
                triangles[baseTriangle + 2] = baseVertex + 2;
                triangles[baseTriangle + 3] = baseVertex + 1;
                triangles[baseTriangle + 4] = baseVertex + 3;
                triangles[baseTriangle + 5] = baseVertex + 2;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.boneWeights = boneWeights;
            mesh.bindposes = bindposes;
            mesh.RecalculateNormals();

            GameObject rendererObject = new GameObject("VatBakeRenderer");
            rendererObject.transform.SetParent(rootObject.transform, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            return renderer;
        }

        /// <summary>A renderer whose mesh has no skinning at all — the accidental-export case.</summary>
        private SkinnedMeshRenderer CreateBonelessRenderer()
        {
            GameObject rootObject = new GameObject("VatBakeBonelessRig");
            spawnedObjects.Add(rootObject);

            Mesh mesh = new Mesh { name = "VatBakeBonelessMesh" };
            spawnedObjects.Add(mesh);
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();

            SkinnedMeshRenderer renderer = rootObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new Transform[0];
            return renderer;
        }
    }
}
