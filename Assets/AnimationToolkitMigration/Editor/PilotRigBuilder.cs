// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit;
using StitchPunk.AnimationToolkit.Authoring;
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
    /// It plays the converted <c>Idle</c> clip, chosen because it is the richest converted clip that
    /// still loops: 12 transform tracks across a full humanoid set, plus 2 sprite tracks. If the
    /// conversion mangled a channel mask, a blend op, or the degrees/radians boundary, a looping
    /// twelve-part idle is where it shows.
    /// </para>
    /// <para>
    /// <strong>The <c>Body</c> part is deliberately included even though <c>Idle</c> never animates
    /// it.</strong> A rig where every part moves cannot distinguish "the clip drove this part" from
    /// "everything is moving"; one deliberately still part makes selective animation visible at a
    /// glance.
    /// </para>
    /// </remarks>
    public static class PilotRigBuilder
    {
        private const string GeneratedFolder = "Assets/AnimationToolkitMigration/Generated";
        private const string ScenePath = "Assets/Scenes/AnimationToolkitPilot.unity";
        private const string SubScenePath = "Assets/Scenes/SubScenes/AnimationToolkitPilotSubScene.unity";
        private const string PilotClipName = "Idle";

        /// <summary>
        /// One part of the pilot figure: which rig target it is, where it sits, how big it is, and
        /// what colour so that each limb is individually trackable by eye.
        /// </summary>
        private struct PilotPart
        {
            public string targetName;
            public Vector3 localPosition;
            public Vector3 localScale;
            public Color color;

            public PilotPart(string targetName, float x, float y, float width, float height, Color color)
            {
                this.targetName = targetName;
                localPosition = new Vector3(x, y, 0f);
                localScale = new Vector3(width, height, 1f);
                this.color = color;
            }
        }

        private static readonly Color TorsoColor = new Color(0.80f, 0.36f, 0.28f);
        private static readonly Color HeadColor = new Color(0.90f, 0.72f, 0.55f);
        private static readonly Color LeftLimbColor = new Color(0.27f, 0.53f, 0.83f);
        private static readonly Color RightLimbColor = new Color(0.36f, 0.72f, 0.42f);

        /// <summary>
        /// The figure, in rig-target names. Left limbs are blue and right limbs green, so a
        /// conversion that swapped a left/right target pair is visible rather than merely plausible.
        /// </summary>
        private static readonly PilotPart[] PilotParts =
        {
            new PilotPart("Body", 0f, 0.90f, 0.50f, 1.00f, TorsoColor),
            new PilotPart("Head", 0f, 1.65f, 0.45f, 0.45f, HeadColor),
            new PilotPart("Pelvis", 0f, 0.35f, 0.45f, 0.30f, TorsoColor),

            new PilotPart("UpperLeftArm", -0.38f, 1.15f, 0.16f, 0.50f, LeftLimbColor),
            new PilotPart("LowerLeftArm", -0.38f, 0.72f, 0.14f, 0.45f, LeftLimbColor),
            new PilotPart("UpperRightArm", 0.38f, 1.15f, 0.16f, 0.50f, RightLimbColor),
            new PilotPart("LowerRightArm", 0.38f, 0.72f, 0.14f, 0.45f, RightLimbColor),

            new PilotPart("UpperLeftLeg", -0.16f, 0.05f, 0.18f, 0.50f, LeftLimbColor),
            new PilotPart("LowerLeftLeg", -0.16f, -0.45f, 0.16f, 0.45f, LeftLimbColor),
            new PilotPart("LeftFoot", -0.16f, -0.74f, 0.24f, 0.12f, LeftLimbColor),
            new PilotPart("UpperRightLeg", 0.16f, 0.05f, 0.18f, 0.50f, RightLimbColor),
            new PilotPart("LowerRightLeg", 0.16f, -0.45f, 0.16f, 0.45f, RightLimbColor),
            new PilotPart("RightFoot", 0.16f, -0.74f, 0.24f, 0.12f, RightLimbColor)
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
            GameObject actorRoot = BuildActor(rig, clipSet, pilotClip, targetIdByName, out int builtParts);
            SceneManager.MoveGameObjectToScene(actorRoot, subScene);
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
                mainCamera.transform.position = new Vector3(0f, 0.6f, -4f);
                mainCamera.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.SaveScene(mainScene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[PilotRigBuilder] Built " + ScenePath + " with " + builtParts + " parts playing the "
                + "converted '" + PilotClipName + "' clip. Open the scene and press Play. The Body "
                + "quad is deliberately unanimated by this clip — everything else should move.");
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
            out int builtParts)
        {
            GameObject actorRoot = new GameObject("PilotActor");

            ActorAuthoring actorAuthoring = actorRoot.AddComponent<ActorAuthoring>();
            actorAuthoring.clipSet = clipSet;

            // Layer 0 is Base. The host's Direction layer was dropped by amendment A37, so the
            // remaining order is Base, Action, Face, Eyes, Mouth, Override — Base keeps index 0.
            StartingLayerState startingLayer = new StartingLayerState();
            startingLayer.layerIndex = 0;
            startingLayer.clip = pilotClip;
            startingLayer.speed = 1f;
            startingLayer.loop = LoopMode.UseClipDefault;
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
                part.GetComponent<MeshRenderer>().sharedMaterial =
                    CreatePartMaterial(pilotPart.targetName, pilotPart.color);

                RigTargetAuthoring targetAuthoring = part.AddComponent<RigTargetAuthoring>();
                targetAuthoring.rig = rig;
                targetAuthoring.targetStableId = targetStableId;
                targetAuthoring.restSliceIndex = 0;
                targetAuthoring.vatDrivingLayerIndex = -1;
                builtParts++;
            }

            return actorRoot;
        }

        private static Material CreatePartMaterial(string partName, Color color)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Universal Render Pipeline/Lit");
            }

            Material material = new Material(unlitShader);
            material.color = color;

            string path = GeneratedFolder + "/PilotMaterials/Pilot" + partName + ".mat";
            if (!AssetDatabase.IsValidFolder(GeneratedFolder + "/PilotMaterials"))
            {
                AssetDatabase.CreateFolder(GeneratedFolder, "PilotMaterials");
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
