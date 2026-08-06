// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StitchPunk.AnimationToolkitSmoke.Editor
{
    /// <summary>
    /// Builds the host-shaped smoke scene the DOTS Animation Toolkit's C4 Definition of Done calls
    /// for — "a subscene with one cutout actor, in this repo, that animates in Play mode".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is host tooling, not package content.</strong> It lives under <c>Assets/</c> with
    /// its own Editor-only assembly precisely so it can never reach the package or a player build.
    /// The package's own shipped samples are a <c>Samples~</c> concern and belong to build step C8.
    /// </para>
    /// <para>
    /// It exists as a re-runnable menu item rather than a one-shot script because the thing it
    /// produces is verified by eye. When the actor looks wrong, the useful question is "what exactly
    /// was it built from", and a committed builder answers that where a hand-assembled scene does
    /// not. It is idempotent: running it again deletes and rebuilds every artefact it owns.
    /// </para>
    /// <para>
    /// <strong>No toolkit shader is used, deliberately.</strong> Build step C5 owns the shaders, and
    /// the transform technique under test here drives <c>LocalTransform</c> and
    /// <c>PostTransformMatrix</c> rather than any material property — so plain URP materials are
    /// sufficient, and using them keeps this scene a test of C4 rather than of C5.
    /// </para>
    /// </remarks>
    public static class SmokeSceneBuilder
    {
        private const string AssetFolder = "Assets/AnimationToolkitSmoke";
        private const string GeneratedFolder = AssetFolder + "/Generated";
        private const string ScenePath = "Assets/Scenes/AnimationToolkitSmoke.unity";
        private const string SubScenePath = "Assets/Scenes/SubScenes/AnimationToolkitSmokeSubScene.unity";

        /// <summary>Names of the three cutout parts, in rig target order.</summary>
        private static readonly string[] TargetNames = { "Torso", "LeftArm", "RightArm" };

        [MenuItem("Tools/DOTS Animation Toolkit/Build Smoke Scene")]
        public static void BuildSmokeScene()
        {
            EnsureFolder("Assets", "AnimationToolkitSmoke");
            EnsureFolder(AssetFolder, "Generated");
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Scenes", "SubScenes");

            RigAsset rig = CreateRig();
            ClipAsset clip = CreateWaveClip(rig);
            ClipSetAsset clipSet = CreateClipSet(rig, clip);

            if (!ReportValidation(rig, clip, clipSet))
            {
                return;
            }

            BuildScenes(rig, clipSet, clip);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[SmokeSceneBuilder] Built " + ScenePath + " with subscene " + SubScenePath
                + ". Open the scene and press Play — the torso should bob and the arms counter-swing.");
        }

        // -------------------------------------------------------------------------------------
        // Assets
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Creates the three-target, one-layer rig.
        /// </summary>
        /// <remarks>
        /// Target stable ids are minted here and written through <see cref="SerializedObject"/>.
        /// <c>RigAsset</c> mints its own id in <c>Awake</c>, but rows appended afterwards still carry
        /// the reserved 0, and the method that fills them is internal to the package. Writing the
        /// serialized field is the sanctioned editor route to it — and <c>StableIdUtility</c> is
        /// public precisely so a tool can mint a conformant id rather than invent one.
        /// </remarks>
        private static RigAsset CreateRig()
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            for (int targetIndex = 0; targetIndex < TargetNames.Length; targetIndex++)
            {
                RigTargetDefinition targetDefinition = new RigTargetDefinition();
                targetDefinition.displayName = TargetNames[targetIndex];
                targetDefinition.kind = TargetKind.Quad;
                targetDefinition.boundsExtents = new float3(0.5f, 0.5f, 0.1f);
                rig.targets.Add(targetDefinition);
            }

            LayerDefinition baseLayer = new LayerDefinition();
            baseLayer.displayName = "Base";
            baseLayer.defaultActive = true;
            rig.layers.Add(baseLayer);

            CreateOrReplaceAsset(rig, GeneratedFolder + "/SmokeRig.asset");

            SerializedObject serializedRig = new SerializedObject(rig);
            SerializedProperty targetsProperty = serializedRig.FindProperty("targets");
            for (int targetIndex = 0; targetIndex < targetsProperty.arraySize; targetIndex++)
            {
                SerializedProperty stableIdProperty = targetsProperty
                    .GetArrayElementAtIndex(targetIndex).FindPropertyRelative("stableId");
                if (stableIdProperty.uintValue == 0u)
                {
                    stableIdProperty.uintValue = StableIdUtility.NewTargetStableId();
                }
            }
            serializedRig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rig);
            return rig;
        }

        /// <summary>
        /// A two-second looping clip: the torso bobs on Y while the arms counter-swing on Z.
        /// </summary>
        /// <remarks>
        /// The two arms are given opposite phase on purpose. A single moving part proves only that
        /// something moved; two parts moving in opposition prove that each part read <em>its own</em>
        /// target's track, which is the failure the source audit found in the host game and the one a
        /// glance at the screen can actually catch.
        /// </remarks>
        private static ClipAsset CreateWaveClip(RigAsset rig)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.rig = rig;
            clip.duration = 2f;
            clip.defaultLoop = LoopMode.Loop;

            float[] bobTimes = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            float[] bobHeights = { 0f, 0.25f, 0f, -0.25f, 0f };
            TransformTrack torsoTrack = NewTrack(rig.targets[0].Id.Value, AnimatedChannels.PositionXY);
            for (int keyIndex = 0; keyIndex < bobTimes.Length; keyIndex++)
            {
                torsoTrack.keys.Add(
                    NewKey(bobTimes[keyIndex], new float3(0f, bobHeights[keyIndex], 0f), 0f));
            }
            clip.transformTracks.Add(torsoTrack);

            // DEGREES, not radians. ClipRegistryBuilder converts once at bake (§4.5 point 2), so the
            // authored value is degrees all the way through. An earlier cut of this file used 0.9
            // here on the assumption it was radians, which is a 0.9-degree swing — present in the
            // data, correct under every test, and invisible on screen. Exactly the kind of value
            // that makes a smoke scene *look* confirmed without having shown anything.
            const float SwingDegrees = 35f;
            float[] swingTimes = { 0f, 0.5f, 1f };
            float[] armSigns = { -1f, 1f };
            for (int armIndex = 0; armIndex < armSigns.Length; armIndex++)
            {
                float sign = armSigns[armIndex];
                float[] swingAngles = { SwingDegrees * sign, -SwingDegrees * sign, SwingDegrees * sign };
                TransformTrack armTrack = NewTrack(
                    rig.targets[armIndex + 1].Id.Value, AnimatedChannels.RotationZ);
                for (int keyIndex = 0; keyIndex < swingTimes.Length; keyIndex++)
                {
                    armTrack.keys.Add(NewKey(swingTimes[keyIndex], float3.zero, swingAngles[keyIndex]));
                }
                clip.transformTracks.Add(armTrack);
            }

            CreateOrReplaceAsset(clip, GeneratedFolder + "/SmokeWave.asset");
            return clip;
        }

        private static ClipSetAsset CreateClipSet(RigAsset rig, ClipAsset clip)
        {
            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.rig = rig;
            clipSet.clips.Add(clip);
            CreateOrReplaceAsset(clipSet, GeneratedFolder + "/SmokeClipSet.asset");
            return clipSet;
        }

        private static TransformTrack NewTrack(uint targetId, AnimatedChannels channels)
        {
            TransformTrack track = new TransformTrack();
            track.targetId = targetId;
            track.blendOp = TrackBlendOp.Override;
            track.channels = channels;
            return track;
        }

        private static TransformKey NewKey(float normalizedTime, float3 position, float rotationZ)
        {
            TransformKey key = new TransformKey();
            key.normalizedTime = normalizedTime;
            key.position = position;
            key.rotationZ = rotationZ;
            key.scale = new float2(1f, 1f);
            key.interpolation = Interpolation.Linear;
            return key;
        }

        /// <summary>
        /// Runs the package's own validation rules over the generated assets and logs every finding.
        /// </summary>
        /// <remarks>
        /// The builder validates rather than assuming its own output is legal, because an invalid
        /// clip set bakes no registry at all (§3.5) — and the symptom of that on screen is an actor
        /// that simply stands still, which is indistinguishable from a broken runtime. Failing here,
        /// loudly, with the rule code, is what keeps a data mistake from being read as a C4 defect.
        /// </remarks>
        private static bool ReportValidation(RigAsset rig, ClipAsset clip, ClipSetAsset clipSet)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            messages.AddRange(ClipValidation.ValidateRig(rig));
            messages.AddRange(ClipValidation.ValidateClip(clip));
            messages.AddRange(ClipValidation.ValidateSet(clipSet));

            bool hasError = false;
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                string line = "[SmokeSceneBuilder] " + message.severity + " " + message.code + ": "
                    + message.text;
                if (message.severity == ValidationSeverity.Error)
                {
                    hasError = true;
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }

            if (hasError)
            {
                Debug.LogError(
                    "[SmokeSceneBuilder] Aborting: the generated assets do not validate, so the bake "
                    + "would produce no clip registry and the actor would stand still for a reason "
                    + "that has nothing to do with the runtime.");
            }
            return !hasError;
        }

        // -------------------------------------------------------------------------------------
        // Scenes
        // -------------------------------------------------------------------------------------

        private static void BuildScenes(RigAsset rig, ClipSetAsset clipSet, ClipAsset clip)
        {
            // The subscene is authored first and saved on its own, because a SubScene component can
            // only point at a SceneAsset that already exists on disk.
            Scene subScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject actorRoot = BuildActor(rig, clipSet, clip);
            SceneManager.MoveGameObjectToScene(actorRoot, subScene);
            EditorSceneManager.SaveScene(subScene, SubScenePath);

            Scene mainScene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject subSceneHolder = new GameObject("AnimationToolkitSmokeSubScene");
            Unity.Scenes.SubScene subSceneComponent = subSceneHolder.AddComponent<Unity.Scenes.SubScene>();
            subSceneComponent.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
            subSceneComponent.AutoLoadScene = true;

            // A 2.5D cutout rig faces +Z, so the default perspective camera is pulled back and
            // levelled rather than left at its tilted default.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0f, 1f, -6f);
                mainCamera.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.SaveScene(mainScene, ScenePath);
        }

        /// <summary>
        /// Builds the actor hierarchy: an <see cref="ActorAuthoring"/> root with three quad parts,
        /// each carrying a <see cref="RigTargetAuthoring"/> bound to its rig target by stable id.
        /// </summary>
        private static GameObject BuildActor(RigAsset rig, ClipSetAsset clipSet, ClipAsset clip)
        {
            GameObject actorRoot = new GameObject("SmokeActor");

            ActorAuthoring actorAuthoring = actorRoot.AddComponent<ActorAuthoring>();
            actorAuthoring.clipSet = clipSet;

            StartingLayerState startingLayer = new StartingLayerState();
            startingLayer.layerIndex = 0;
            startingLayer.clip = clip;
            startingLayer.speed = 1f;
            startingLayer.loop = LoopMode.UseClipDefault;
            actorAuthoring.startingLayers.Add(startingLayer);

            Vector3[] restPositions =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(-0.75f, 0.35f, 0f),
                new Vector3(0.75f, 0.35f, 0f)
            };
            Vector3[] partScales =
            {
                new Vector3(1f, 1.5f, 1f),
                new Vector3(0.35f, 1f, 1f),
                new Vector3(0.35f, 1f, 1f)
            };
            Color[] partColors =
            {
                new Color(0.85f, 0.35f, 0.25f),
                new Color(0.25f, 0.55f, 0.85f),
                new Color(0.35f, 0.75f, 0.4f)
            };

            for (int targetIndex = 0; targetIndex < TargetNames.Length; targetIndex++)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
                part.name = TargetNames[targetIndex];
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(actorRoot.transform, false);
                part.transform.localPosition = restPositions[targetIndex];
                part.transform.localScale = partScales[targetIndex];

                Material partMaterial = CreatePartMaterial(
                    TargetNames[targetIndex], partColors[targetIndex]);
                part.GetComponent<MeshRenderer>().sharedMaterial = partMaterial;

                RigTargetAuthoring targetAuthoring = part.AddComponent<RigTargetAuthoring>();
                targetAuthoring.rig = rig;
                targetAuthoring.targetStableId = rig.targets[targetIndex].Id.Value;
                targetAuthoring.restSliceIndex = 0;
                targetAuthoring.vatDrivingLayerIndex = -1;
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
            CreateOrReplaceAsset(material, GeneratedFolder + "/Smoke" + partName + ".mat");
            return material;
        }

        // -------------------------------------------------------------------------------------
        // Asset plumbing
        // -------------------------------------------------------------------------------------

        private static void EnsureFolder(string parentFolder, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
        }

        /// <summary>
        /// Writes an asset, replacing any previous one at the same path so the builder is idempotent.
        /// </summary>
        private static void CreateOrReplaceAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
