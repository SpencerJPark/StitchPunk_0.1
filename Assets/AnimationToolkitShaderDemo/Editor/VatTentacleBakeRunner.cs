// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit;
using StitchPunk.AnimationToolkit.Authoring;
using StitchPunk.AnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo.Editor
{
    /// <summary>
    /// Bakes the procedural tentacle through <see cref="VatTextureBaker"/> and saves the result as
    /// a usable <see cref="VatTextureSetAsset"/> — the first end-to-end exercise of the VAT path.
    /// </summary>
    /// <remarks>
    /// The host has no skinned meshes (its characters are layered quads), so VAT has no existing
    /// content to validate against. This generates its own subject rather than waiting for one,
    /// which is the only way the bake path gets tested before a buyer finds it.
    /// </remarks>
    public static class VatTentacleBakeRunner
    {
        private const string OutputFolder = "Assets/AnimationToolkitShaderDemo/Generated/Vat";
        private const ulong TentacleClipId = 0x00000000000A0001UL;

        [MenuItem("Tools/DOTS Animation Toolkit/Bake VAT Tentacle")]
        public static void BakeTentacle()
        {
            EnsureFolder("Assets/AnimationToolkitShaderDemo", "Generated");
            EnsureFolder("Assets/AnimationToolkitShaderDemo/Generated", "Vat");

            AnimationClip waveClip;
            SkinnedMeshRenderer renderer = VatTentacleRigBuilder.CreateTentacle("VatTentacleSource", out waveClip);

            VatBakeInput bakeInput = new VatBakeInput
            {
                skinnedMeshRenderer = renderer,
                flavor = VatFlavor.BoneMatrix,
                samplesPerSecond = VatTentacleRigBuilder.BakeSampleRate,
                useFullPrecision = false,
                clips = new List<VatBakeClip>
                {
                    new VatBakeClip
                    {
                        clipId = TentacleClipId,
                        animationClip = waveClip,

                        // The wave returns to its start, so the duplicated final frame lets the
                        // shader lerp across the seam without reading another clip's first row.
                        loopSafe = true
                    }
                }
            };

            VatBakeResult bakeResult;
            bool baked = VatTextureBaker.Bake(bakeInput, out bakeResult);

            if (!baked)
            {
                Debug.LogError("[VatTentacleBake] " + bakeResult.message);
                Object.DestroyImmediate(renderer.transform.root.gameObject);
                return;
            }

            SaveBake(renderer, waveClip, bakeResult);
            Object.DestroyImmediate(renderer.transform.root.gameObject);

            Debug.Log(
                "[VatTentacleBake] " + bakeResult.message
                + " bones=" + bakeResult.boneCount.ToString()
                + " width=" + bakeResult.textureWidth.ToString()
                + " rowsPerFrame=" + bakeResult.rowsPerFrame.ToString()
                + " ranges=" + bakeResult.clipRanges.Count.ToString()
                + " sourceHash=0x" + bakeResult.sourceHash.ToString("X16"));
        }

        private static void SaveBake(
            SkinnedMeshRenderer renderer, AnimationClip waveClip, VatBakeResult bakeResult)
        {
            AssetDatabase.CreateAsset(bakeResult.boneOrPositionTexture, OutputFolder + "/TentacleVat.asset");

            // The mesh must be saved too: the runtime renders THIS mesh, whose bone weights and UVs
            // are what the baked texture is addressed against. A different mesh with the same
            // silhouette would sample the wrong rows.
            Mesh runtimeMesh = Object.Instantiate(renderer.sharedMesh);
            runtimeMesh.name = "TentacleRuntimeMesh";
            AssetDatabase.CreateAsset(runtimeMesh, OutputFolder + "/TentacleRuntimeMesh.asset");

            AnimationClip savedClip = Object.Instantiate(waveClip);
            savedClip.name = "TentacleWave";
            AssetDatabase.CreateAsset(savedClip, OutputFolder + "/TentacleWave.anim");

            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            textureSet.flavor = VatFlavor.BoneMatrix;
            textureSet.boneTexture = bakeResult.boneOrPositionTexture;
            textureSet.runtimeMesh = runtimeMesh;
            textureSet.boneCount = bakeResult.boneCount;
            textureSet.vertexCount = bakeResult.vertexCount;
            textureSet.textureWidth = bakeResult.textureWidth;
            textureSet.rowsPerFrame = bakeResult.rowsPerFrame;
            textureSet.sourceHash = bakeResult.sourceHash;
            textureSet.clipRanges = bakeResult.clipRanges;

            AssetDatabase.CreateAsset(textureSet, OutputFolder + "/TentacleVatSet.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string parentFolder, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
        }
    }
}
