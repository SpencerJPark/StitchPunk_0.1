// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Generates the sample tentacle: a prefab plus the clip, clip-set and rig assets needed to run
    /// it through the VAT baker and open it in the Clip Editor, so a project with no VAT content has
    /// something to bake in one call.
    /// </summary>
    public static class VatSampleTentacleUtility
    {
        public static bool CreateSampleAssets(
            string assetFolder,
            out ClipSetAsset clipSet,
            out RigAsset rig,
            out GameObject samplePrefab,
            out string failureMessage)
        {
            clipSet = null;
            rig = null;
            samplePrefab = null;
            failureMessage = string.Empty;

            if (string.IsNullOrEmpty(assetFolder))
            {
                failureMessage = "Asset folder path must not be empty.";
                return false;
            }

            GameObject temporaryRoot = null;
            try
            {
                EnsureFolderPath(assetFolder);

                List<BoneTrack> waveBoneTracks;
                SkinnedMeshRenderer tentacleRenderer =
                    VatTentacleRigBuilder.CreateTentacle("VatSampleTentacle", out waveBoneTracks);
                temporaryRoot = tentacleRenderer.transform.root.gameObject;

                // Both the procedural mesh and the generated material have to become real assets
                // before the prefab is written: PrefabUtility cannot serialise a reference to an
                // in-memory object, so either one left unsaved comes back null inside the prefab.
                CreateOrReplaceAsset(tentacleRenderer.sharedMesh, assetFolder + "/VatSampleTentacleMesh.asset");
                CreateOrReplaceAsset(tentacleRenderer.sharedMaterial, assetFolder + "/VatSampleTentacleMaterial.mat");
                AssetDatabase.SaveAssets();
                tentacleRenderer.sharedMesh =
                    AssetDatabase.LoadAssetAtPath<Mesh>(assetFolder + "/VatSampleTentacleMesh.asset");
                tentacleRenderer.sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(assetFolder + "/VatSampleTentacleMaterial.mat");

                string prefabPath = assetFolder + "/VatSampleTentacle.prefab";
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null)
                {
                    AssetDatabase.DeleteAsset(prefabPath);
                }
                samplePrefab = PrefabUtility.SaveAsPrefabAsset(temporaryRoot, prefabPath);

                ClipAsset clipAsset = ScriptableObject.CreateInstance<ClipAsset>();
                clipAsset.duration = VatTentacleRigBuilder.WaveDuration;
                clipAsset.frameRate = VatTentacleRigBuilder.BakeSampleRate;
                clipAsset.boneTracks = waveBoneTracks;
                // No sourceClip: the bone tracks above are the animation. The VAT settings still
                // carry loopSafe, which is what appends the duplicated final frame the shader needs
                // to interpolate across the loop point.
                clipAsset.vatSource = new VatClipSource
                {
                    sourceClip = null,
                    sampleFps = VatTentacleRigBuilder.BakeSampleRate,
                    loopSafe = true
                };
                clipAsset.EnsureStableIds();
                CreateOrReplaceAsset(clipAsset, assetFolder + "/VatSampleWave.asset");

                ClipSetAsset clipSetAsset = ScriptableObject.CreateInstance<ClipSetAsset>();
                clipSetAsset.clips = new List<ClipAsset> { clipAsset };
                CreateOrReplaceAsset(clipSetAsset, assetFolder + "/VatSampleTentacleClips.asset");

                // The rig is written last because it has to point at the prefab. A rig with a null
                // sourcePrefab has nothing for the Clip Editor to instantiate, so the sample opens
                // as an empty viewport — the Skinned Source field resolves the prefab through here.
                // No targets: this is a skinned chain driven by bone tracks, not cutout parts.
                string rigPath = assetFolder + "/VatSampleTentacleRig.asset";
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rigPath) != null)
                {
                    AssetDatabase.DeleteAsset(rigPath);
                }
                RigAsset rigAsset = RigAssetUtility.CreateRig(rigPath, samplePrefab, null);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                clipSet = clipSetAsset;
                rig = rigAsset;
                failureMessage = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                clipSet = null;
                rig = null;
                samplePrefab = null;
                failureMessage = exception.Message;
                return false;
            }
            finally
            {
                // The deliverable is the prefab; the scene copy was only somewhere to author it.
                if (temporaryRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryRoot);
                }
            }
        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string accumulated = segments[0];
            for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                string next = accumulated + "/" + segments[segmentIndex];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(accumulated, segments[segmentIndex]);
                }
                accumulated = next;
            }
        }

        private static void CreateOrReplaceAsset(UnityEngine.Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
