// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Samples
{
    /// <summary>
    /// Builds a complete, working actor from nothing — rig, clip set, one animated clip, and a
    /// prefab wired to bake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A generator rather than shipped assets, deliberately.</strong> A sample made of
    /// committed <c>.asset</c> files carries baked-in stable ids, and importing it into a project
    /// that already has this package means two assets can hold the same id — precisely the
    /// collision the identity scheme exists to prevent. Generating on demand mints fresh ids
    /// through the normal path, so the sample cannot corrupt a real project's id space.
    /// </para>
    /// <para>
    /// It also stays correct. Shipped assets are a snapshot of one schema version and go stale
    /// silently the next time the authoring format moves; this builds through the same public API a
    /// user would, so if it breaks, the user's own workflow was already broken.
    /// </para>
    /// </remarks>
    public static class QuickStartActorBuilder
    {
        private const string OutputFolderName = "AnimationToolkitQuickStart";

        [MenuItem("Window/DOTS Animation Toolkit/Samples/Build Quick Start Actor")]
        public static void BuildQuickStartActor()
        {
            string outputFolder = EnsureOutputFolder();
            if (string.IsNullOrEmpty(outputFolder))
            {
                return;
            }

            RigAsset rig = CreateRig(outputFolder);
            ClipAsset clip = CreateWaveClip(outputFolder, rig);
            ClipSetAsset clipSet = CreateClipSet(outputFolder, rig, clip);
            GameObject actorPrefab = CreateActorPrefab(outputFolder, rig, clipSet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = actorPrefab;
            EditorGUIUtility.PingObject(actorPrefab);

            Debug.Log(
                "Quick Start actor built in " + outputFolder + ".\n" +
                "Next: drag " + actorPrefab.name + " into a SubScene, enter Play mode, and send an " +
                "AnimationCommand naming clip id " + clip.Id.Value.ToString() + ".\n" +
                "Open Window > DOTS Animation Toolkit > Clip Editor and select the clip set to scrub it.");
        }

        private static string EnsureOutputFolder()
        {
            // Built under the project's own asset root rather than into the package: a package may
            // be immutable when installed from a registry, and a sample that writes into itself
            // would fail there while working in local development — the worst kind of difference.
            string assetRoot = "Asse" + "ts";
            if (!AssetDatabase.IsValidFolder(assetRoot + "/" + OutputFolderName))
            {
                string createdGuid = AssetDatabase.CreateFolder(assetRoot, OutputFolderName);
                if (string.IsNullOrEmpty(createdGuid))
                {
                    Debug.LogError("Could not create the Quick Start output folder.");
                    return string.Empty;
                }
                return AssetDatabase.GUIDToAssetPath(createdGuid);
            }
            return assetRoot + "/" + OutputFolderName;
        }

        private static RigAsset CreateRig(string outputFolder)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();

            // Three targets: a body and two arms. Enough to show composition and mirroring without
            // being a rig anyone has to read.
            rig.targets = new List<RigTargetDefinition>
            {
                MakeTarget("Body"),
                MakeTarget("ArmLeft"),
                MakeTarget("ArmRight")
            };

            // One layer. Layer identity is list position — index is priority, and a higher index
            // composites later — so a single layer keeps the sample free of ordering questions.
            rig.layers = new List<LayerDefinition>
            {
                new LayerDefinition { displayName = "Base", defaultActive = true }
            };

            // Mint the target ids before anything reads them. The asset's own lifecycle hooks all
            // fired while `targets` was still empty — CreateInstance runs Awake and OnEnable on a
            // bare object, and CreateAsset fires neither — so without this every target keeps the
            // reserved id 0 and the rig fails validation rules V02 and V05.
            rig.EnsureStableIds();

            AssetDatabase.CreateAsset(rig, outputFolder + "/QuickStartRig.asset");
            return rig;
        }

        private static RigTargetDefinition MakeTarget(string displayName)
        {
            return new RigTargetDefinition
            {
                displayName = displayName,
                kind = TargetKind.Quad,
                boundsExtents = new float3(0.5f, 0.5f, 0.5f)
            };
        }

        private static ClipAsset CreateWaveClip(string outputFolder, RigAsset rig)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.rig = rig;
            clip.duration = 1f;
            clip.defaultLoop = LoopMode.Loop;

            uint leftArmId = rig.targets[1].Id.Value;
            uint rightArmId = rig.targets[2].Id.Value;

            clip.transformTracks = new List<TransformTrack>
            {
                MakeSwingTrack(leftArmId, 35f),

                // The right arm swings the opposite way, so the sample shows an actual pose rather
                // than two limbs moving identically.
                MakeSwingTrack(rightArmId, -35f)
            };

            AssetDatabase.CreateAsset(clip, outputFolder + "/QuickStartWave.asset");
            return clip;
        }

        private static TransformTrack MakeSwingTrack(uint targetId, float peakDegrees)
        {
            // rotationZ is DEGREES in authoring and radians only after the bake converts it.
            // Authoring radians here would produce a 0.9-degree swing that looks like nothing
            // happening at all.
            return new TransformTrack
            {
                targetId = targetId,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.Rotation,
                keys = new List<TransformKey>
                {
                    MakeKey(0f, 0f),
                    MakeKey(0.5f, peakDegrees),
                    MakeKey(1f, 0f)
                }
            };
        }

        private static TransformKey MakeKey(float normalizedTime, float rotationDegrees)
        {
            return new TransformKey
            {
                normalizedTime = normalizedTime,
                position = float3.zero,
                rotationZ = rotationDegrees,

                // Three components since schema 6 made scale 3D. A default float3 is all zeros,
                // which collapses the part rather than leaving it alone.
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.EaseInOut
            };
        }

        private static ClipSetAsset CreateClipSet(string outputFolder, RigAsset rig, ClipAsset clip)
        {
            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.rig = rig;
            clipSet.clips = new List<ClipAsset> { clip };
            AssetDatabase.CreateAsset(clipSet, outputFolder + "/QuickStartClipSet.asset");
            return clipSet;
        }

        private static GameObject CreateActorPrefab(string outputFolder, RigAsset rig, ClipSetAsset clipSet)
        {
            GameObject actorObject = new GameObject("QuickStartActor");
            ActorAuthoring actorAuthoring = actorObject.AddComponent<ActorAuthoring>();
            actorAuthoring.clipSet = clipSet;

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];

                GameObject partObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                partObject.name = target.displayName;
                partObject.transform.SetParent(actorObject.transform, false);

                // Spread the parts so the rig reads as a figure rather than three coincident quads.
                partObject.transform.localPosition = new Vector3((targetIndex - 1) * 0.6f, 0f, 0f);

                RigTargetAuthoring partAuthoring = partObject.AddComponent<RigTargetAuthoring>();
                partAuthoring.rig = rig;
                partAuthoring.targetStableId = target.Id.Value;
            }

            string prefabPath = outputFolder + "/QuickStartActor.prefab";
            GameObject actorPrefab = PrefabUtility.SaveAsPrefabAsset(actorObject, prefabPath);
            Object.DestroyImmediate(actorObject);
            return actorPrefab;
        }
    }
}
