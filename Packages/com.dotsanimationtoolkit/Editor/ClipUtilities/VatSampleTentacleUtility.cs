// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Generates a small in-scene tentacle rig plus the clip/clip-set/rig assets needed to run it
    /// through the VAT baker, so a project with no VAT content can preview a bake in one call.
    /// </summary>
    public static class VatSampleTentacleUtility
    {
        public static bool CreateSampleAssets(
            string assetFolder,
            out ClipSetAsset clipSet,
            out RigAsset rig,
            out SkinnedMeshRenderer renderer,
            out string failureMessage)
        {
            clipSet = null;
            rig = null;
            renderer = null;
            failureMessage = string.Empty;

            if (string.IsNullOrEmpty(assetFolder))
            {
                failureMessage = "Asset folder path must not be empty.";
                return false;
            }

            try
            {
                EnsureFolderPath(assetFolder);

                SkinnedMeshRenderer tentacleRenderer = VatTentacleRigBuilder.CreateTentacle("VatSampleTentacle", out AnimationClip waveClip);
                Undo.RegisterCreatedObjectUndo(tentacleRenderer.transform.root.gameObject, "Create Sample Tentacle");

                CreateOrReplaceAsset(waveClip, assetFolder + "/VatSampleTentacleWave.anim");

                ClipAsset clipAsset = ScriptableObject.CreateInstance<ClipAsset>();
                clipAsset.duration = waveClip.length;
                clipAsset.frameRate = VatTentacleRigBuilder.BakeSampleRate;
                clipAsset.vatSource = new VatClipSource
                {
                    sourceClip = waveClip,
                    sampleFps = VatTentacleRigBuilder.BakeSampleRate,
                    loopSafe = true
                };
                clipAsset.EnsureStableIds();
                CreateOrReplaceAsset(clipAsset, assetFolder + "/VatSampleWave.asset");

                ClipSetAsset clipSetAsset = ScriptableObject.CreateInstance<ClipSetAsset>();
                clipSetAsset.clips = new List<ClipAsset> { clipAsset };
                CreateOrReplaceAsset(clipSetAsset, assetFolder + "/VatSampleTentacleClips.asset");

                RigAsset rigAsset = ScriptableObject.CreateInstance<RigAsset>();
                rigAsset.EnsureStableIds();
                CreateOrReplaceAsset(rigAsset, assetFolder + "/VatSampleTentacleRig.asset");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                clipSet = clipSetAsset;
                rig = rigAsset;
                renderer = tentacleRenderer;
                failureMessage = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                clipSet = null;
                rig = null;
                renderer = null;
                failureMessage = exception.Message;
                return false;
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
