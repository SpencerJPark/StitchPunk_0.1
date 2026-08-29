// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using StitchPunk.AnimationToolkitMigration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StitchPunk.AnimationToolkitMigration.Editor
{
    /// <summary>
    /// Stands up one actor on the <em>converted</em> host content — step 2 of the §13.2 cutover
    /// order, the point at which the migration stops being a file-format exercise and has to
    /// actually move on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is a fresh rig, not a host prefab</strong> (owner decision, 2026-08-06). No
    /// game content is touched, so the old pipeline keeps running exactly as it did and there is
    /// nothing to unwind if the converted data turns out to be wrong. What it gives up is testing
    /// the host's *actual* rig hierarchy; what it buys is that a bad conversion cannot break a
    /// prefab the game depends on.
    /// </para>
    /// <para>
    /// It plays the converted <c>HumanBlinkNormal</c> clip on the Eyes layer, against the host's
    /// real unit materials and texture arrays. That combination is what makes the flipbook path
    /// observable: the clip drives genuine slice changes on the eye (53 → 11 → 9) and the art is
    /// asymmetric, so a mirror or a wrong frame reads as wrong rather than merely plausible.
    /// </para>
    /// </remarks>
    public static class PilotRigBuilder
    {
        private const string GeneratedFolder = "Assets/AnimationToolkitMigration/Generated";
        private const string ScenePath = "Assets/Scenes/AnimationToolkitPilot.unity";
        private const string SubScenePath = "Assets/Scenes/SubScenes/AnimationToolkitPilotSubScene.unity";
        private const string PilotClipName = "HumanBlinkNormal";

        /// <summary>
        /// The layer the blink plays on. After amendment A37 removed <c>Direction</c>, the rig's
        /// layers are Base(0), Action(1), Face(2), Eyes(3), Mouth(4), Override(5).
        /// </summary>
        private const int EyesLayerIndex = 3;

        private const string HostMaterialFolder = "Assets/Materials/Units/";

        /// <summary>
        /// The clip the driver crossfades to and from. Angry differs from Normal in its eyebrow
        /// transform tracks, so the blend is visible as eyebrows sliding between two poses — sprite
        /// frames never blend by design (§10 answer 2), so a slice-only pair would show nothing.
        /// </summary>
        private const string SecondClipName = "HumanBlinkAngry";

        /// <summary>
        /// One part of the pilot face: which rig target it is, where it sits, which host material
        /// supplies its texture array, and the slice it rests on.
        /// </summary>
        private struct PilotPart
        {
            public string targetName;
            public Vector3 localPosition;
            public Vector3 localScale;
            public string materialName;
            public int restSliceIndex;

            public PilotPart(
                string targetName, float x, float y, float z,
                float width, float height, string materialName, int restSliceIndex)
            {
                this.targetName = targetName;
                localPosition = new Vector3(x, y, z);
                localScale = new Vector3(width, height, 1f);
                this.materialName = materialName;
                this.restSliceIndex = restSliceIndex;
            }
        }

        /// <summary>
        /// A face rather than a stick figure, built from the host's <em>real</em> unit materials and
        /// texture arrays.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The first pilot was a body in solid colours and it could not fail informatively.</strong>
        /// A flat-coloured quad looks identical whether its slice changed, whether it was mirrored,
        /// or whether nothing happened at all — so "the arms move" was the only signal it could give,
        /// and the entire flipbook path (which is where amendment A37 lives) went unexercised.
        /// </para>
        /// <para>
        /// A face fixes all three at once. <c>HumanBlinkNormal</c> drives genuine slice changes on
        /// the eye — 53 → 11 → 9, an open/mid/closed blink — so the flipbook path is visible rather
        /// than assumed. And ears, noses and hair are <em>asymmetric</em> art, which is what makes a
        /// mirror judgeable: a solid rectangle mirrored is the same rectangle.
        /// </para>
        /// <para>
        /// z is authored per part so the features sit in front of the head rather than z-fighting
        /// with it. That column is also the draw-order channel a per-direction clip would animate.
        /// </para>
        /// </remarks>
        private static readonly PilotPart[] PilotParts =
        {
            new PilotPart("Head", 0f, 0f, 0.30f, 2.0f, 2.0f, "Head", 0),
            new PilotPart("Hair", 0f, 0.62f, 0.20f, 2.1f, 1.2f, "MaleHair", 0),
            new PilotPart("Ear", -0.92f, 0.05f, 0.25f, 0.5f, 0.6f, "Ear", 0),

            new PilotPart("LeftEyebrow", -0.36f, 0.44f, 0.10f, 0.55f, 0.28f, "Eyebrows", 0),
            new PilotPart("RightEyebrow", 0.36f, 0.44f, 0.10f, 0.55f, 0.28f, "Eyebrows", 0),
            new PilotPart("LeftEye", -0.36f, 0.14f, 0.10f, 0.50f, 0.50f, "MaleEyes", 0),
            new PilotPart("RightEye", 0.36f, 0.14f, 0.10f, 0.50f, 0.50f, "MaleEyes", 0),

            new PilotPart("Nose", 0f, -0.14f, 0.05f, 0.38f, 0.55f, "Nose", 0),
            new PilotPart("Mouth", 0f, -0.58f, 0.10f, 0.70f, 0.35f, "Mouth", 0)
        };

        [MenuItem("Tools/DOTS Animation Toolkit/Migration/Build Pilot Rig")]
        public static void BuildPilotRig()
        {
            RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(GeneratedFolder + "/HumanoidRig.asset");
            ClipSetAsset clipSet =
                AssetDatabase.LoadAssetAtPath<ClipSetAsset>(GeneratedFolder + "/HostClipSet.asset");
            ClipAsset pilotClip =
                AssetDatabase.LoadAssetAtPath<ClipAsset>(GeneratedFolder + "/" + PilotClipName + ".asset");

            if (rig == null || clipSet == null || pilotClip == null)
            {
                Debug.LogError(
                    "[PilotRigBuilder] Converted assets not found in " + GeneratedFolder +
                    ". Run Tools > DOTS Animation Toolkit > Migration > Convert Host Clips first.");
                return;
            }

            Dictionary<string, uint> targetIdByName = BuildTargetLookup(rig);
            Scene subScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Two identical faces side by side, the right-hand one baked mirrored. Same art, same
            // clip, same everything else — so the ONLY difference on screen is the mirror, which is
            // what makes it judgeable. A single mirrored face proves nothing: you cannot tell a
            // reflected nose from a nose that was always drawn that way without the original beside
            // it.
            GameObject plainActor = BuildActor(
                rig, clipSet, pilotClip, targetIdByName, "PilotActor",
                new Vector3(-1.35f, 0f, 0f), false, out int builtParts);

            // The driver goes on the LEFT face only. The right one stays a fixed mirrored reference,
            // so there is always a still frame to compare the moving one against — the same reason
            // the pair exists at all.
            ClipAsset secondClip =
                AssetDatabase.LoadAssetAtPath<ClipAsset>(GeneratedFolder + "/" + SecondClipName + ".asset");
            PilotDriverAuthoring driverAuthoring = plainActor.AddComponent<PilotDriverAuthoring>();
            driverAuthoring.layerIndex = EyesLayerIndex;
            driverAuthoring.firstClip = pilotClip;
            driverAuthoring.secondClip = secondClip != null ? secondClip : pilotClip;
            GameObject mirroredActor = BuildActor(
                rig, clipSet, pilotClip, targetIdByName, "PilotActorMirrored",
                new Vector3(1.35f, 0f, 0f), true, out int _);
            SceneManager.MoveGameObjectToScene(plainActor, subScene);
            SceneManager.MoveGameObjectToScene(mirroredActor, subScene);
            EditorSceneManager.SaveScene(subScene, SubScenePath);

            Scene mainScene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject subSceneHolder = new GameObject("AnimationToolkitPilotSubScene");
            Unity.Scenes.SubScene subSceneComponent = subSceneHolder.AddComponent<Unity.Scenes.SubScene>();
            subSceneComponent.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
            subSceneComponent.AutoLoadScene = true;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0f, 0f, -5f);
                mainCamera.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.SaveScene(mainScene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[PilotRigBuilder] Built " + ScenePath + " with " + builtParts + " parts playing the "
                + "converted '" + PilotClipName + "' clip on the Eyes layer. Open the scene and press "
                + "Play: both faces blink through real texture-array slices (53 -> 11 -> 9), and the "
                + "RIGHT-HAND face is permanently mirrored as a reference. The LEFT face is driven: "
                + "it crossfades between two blinks every 3s (watch the eyebrows slide) and flips "
                + "mirror every 4s, so facing is visibly live state rather than a bake-time value.");
        }

        private static Dictionary<string, uint> BuildTargetLookup(RigAsset rig)
        {
            Dictionary<string, uint> targetIdByName = new Dictionary<string, uint>();
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition != null && !targetIdByName.ContainsKey(targetDefinition.displayName))
                {
                    targetIdByName.Add(targetDefinition.displayName, targetDefinition.Id.Value);
                }
            }
            return targetIdByName;
        }

        private static GameObject BuildActor(
            RigAsset rig,
            ClipSetAsset clipSet,
            ClipAsset pilotClip,
            Dictionary<string, uint> targetIdByName,
            string actorName,
            Vector3 actorPosition,
            bool startMirrored,
            out int builtParts)
        {
            GameObject actorRoot = new GameObject(actorName);
            actorRoot.transform.position = actorPosition;

            ActorAuthoring actorAuthoring = actorRoot.AddComponent<ActorAuthoring>();
            actorAuthoring.rig = rig;
            actorAuthoring.clipSets = new List<ClipSetAsset> { clipSet };

            // Layer 0 is Base. The host's Direction layer was dropped by amendment A37, so the
            // remaining order is Base, Action, Face, Eyes, Mouth, Override — Base keeps index 0.
            StartingLayerState startingLayer = new StartingLayerState();
            startingLayer.layerIndex = EyesLayerIndex;
            startingLayer.clip = pilotClip;
            startingLayer.speed = 1f;

            // Forced to Loop: the host authored blinks as one-shots, and a Once clip finishes in
            // under a second and then holds — which on screen is indistinguishable from a flipbook
            // path that never worked. Looping makes the slice changes repeat so they can be watched.
            startingLayer.loop = LoopMode.Loop;
            actorAuthoring.startingLayers.Add(startingLayer);

            builtParts = 0;
            for (int partIndex = 0; partIndex < PilotParts.Length; partIndex++)
            {
                PilotPart pilotPart = PilotParts[partIndex];
                if (!targetIdByName.TryGetValue(pilotPart.targetName, out uint targetStableId))
                {
                    Debug.LogWarning(
                        "[PilotRigBuilder] Rig has no target named '" + pilotPart.targetName +
                        "'; skipping that part.");
                    continue;
                }

                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
                part.name = pilotPart.targetName;
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(actorRoot.transform, false);
                part.transform.localPosition = pilotPart.localPosition;
                part.transform.localScale = pilotPart.localScale;

                // The host's own material, not a generated one. The toolkit's SpriteSliceProperty is
                // [MaterialProperty("_ImageIndex")] — the exact name the host's array shaders already
                // read — so the package drives existing host art with no shader work at all. That is
                // §10 answer 11's "hosts keep their own graphs and consume the property names",
                // holding in practice rather than only on paper, and it is why a real flipbook test
                // is possible before C5 ships any shaders.
                Material hostMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    HostMaterialFolder + pilotPart.materialName + ".mat");
                if (hostMaterial == null)
                {
                    Debug.LogWarning(
                        "[PilotRigBuilder] Host material '" + pilotPart.materialName +
                        "' not found; '" + pilotPart.targetName + "' will render untextured.");
                }
                else
                {
                    part.GetComponent<MeshRenderer>().sharedMaterial = hostMaterial;
                }

                RigTargetAuthoring targetAuthoring = part.AddComponent<RigTargetAuthoring>();
                targetAuthoring.rig = rig;
                targetAuthoring.targetStableId = targetStableId;
                targetAuthoring.restSliceIndex = pilotPart.restSliceIndex;
                targetAuthoring.vatDrivingLayerIndex = -1;
                targetAuthoring.startMirrored = startMirrored;
                builtParts++;
            }

            return actorRoot;
        }

    }
}
