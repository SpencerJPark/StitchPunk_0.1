using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace StitchPunk.Editor.ContentAuthoring
{
    // One-shot, idempotent content-authoring recipe for G5-P5/P6/P7 (ActorProfileCutover_System.md).
    // Each method is meant to be run once, in order, from the Editor (menu items below); re-running
    // any of them must not duplicate targets, tags, clips or profile rows.
    public static class MaleCitizenContentAuthoring
    {
        private const string RigPath = "Assets/ScriptableObjects/Animations/NewRig.asset";
        private const string ClipSetPath = "Assets/ScriptableObjects/Animations/NewClipSet.asset";
        private const string PrefabPath = "Assets/Prefabs/Units/MaleCitizen.prefab";
        private const string ProfilePath = "Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset";
        private const string WalkDirectionSetPath = "Assets/ScriptableObjects/Animations/MaleCitizenWalkDirectionSet.asset";
        private const string OldWalkDirectionSetPath = "Assets/ScriptableObjects/Animations/MaleCitizenWalkDirections.asset";
        private const string CutscenePath = "Assets/ScriptableObjects/Animations/A65CheckpointCutscene.asset";
        private const string MaleCitizenUnitPath = "Assets/ScriptableObjects/Units/MaleCitizen.asset";
        private const string RotterUnitPath = "Assets/ScriptableObjects/Units/Rotter.asset";

        private static readonly string[] FacePartPaths =
        {
            "Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/Eyes",
            "Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/Mouth",
            "Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/LeftEyeBrow",
            "Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/RightEyeBrow",
        };

        private static readonly string[] FaceTagNames = { "Eyes", "Mouth", "LeftEyebrow", "RightEyebrow" };

        [MenuItem("Stitch Punk/Content Authoring/G5 P5 - Add Face Targets")]
        public static string AddFaceTargets()
        {
            RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(RigPath);
            if (rig == null)
            {
                return "FAILED: " + RigPath + " not found.";
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Dictionary<string, uint> mintedTagIdByName = new Dictionary<string, uint>();
                for (int tagIndex = 0; tagIndex < FaceTagNames.Length; tagIndex++)
                {
                    mintedTagIdByName[FaceTagNames[tagIndex]] = MintOrReuseTargetTag(FaceTagNames[tagIndex]);
                }
                RegenerateTargetTagsConstants();

                int addedTargetCount = 0;
                for (int partIndex = 0; partIndex < FacePartPaths.Length; partIndex++)
                {
                    string nodePath = FacePartPaths[partIndex];
                    string partName = nodePath.Substring(nodePath.LastIndexOf('/') + 1);
                    uint tagId = mintedTagIdByName[FaceTagNames[partIndex]];

                    bool alreadyPresent = rig.targets.Any(existingTarget => existingTarget.sourceNodePath == nodePath);
                    if (!alreadyPresent)
                    {
                        Transform partTransform = prefabRoot.transform.Find(nodePath);
                        if (partTransform == null)
                        {
                            Debug.LogWarning("MaleCitizenContentAuthoring: prefab has no node at '" + nodePath + "'.");
                            continue;
                        }

                        MeshFilter meshFilter = partTransform.GetComponent<MeshFilter>();
                        float3 boundsExtents = new float3(0.5f, 0.5f, 0.02f);
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            Bounds meshBounds = meshFilter.sharedMesh.bounds;
                            Vector3 centerPlusExtents = new Vector3(
                                Mathf.Abs(meshBounds.center.x) + meshBounds.extents.x,
                                Mathf.Abs(meshBounds.center.y) + meshBounds.extents.y,
                                0.02f);
                            boundsExtents = new float3(centerPlusExtents.x, centerPlusExtents.y, 0.02f);
                        }

                        RigTargetDefinition newTarget = new RigTargetDefinition
                        {
                            displayName = partName,
                            sourceNodePath = nodePath,
                            kind = TargetKind.Quad,
                            boundsExtents = boundsExtents,
                            facesDirection = false,
                            tagId = tagId,
                        };
                        rig.targets.Add(newTarget);
                        addedTargetCount++;
                    }
                }

                rig.EnsureStableIds();

                bool hasEyebrowMirrorPair = rig.mirrorPairs != null && rig.mirrorPairs.Any(pair =>
                    IsFaceTagPair(rig, pair, "LeftEyebrow", "RightEyebrow"));
                if (!hasEyebrowMirrorPair)
                {
                    RigTargetDefinition leftEyebrowTarget = rig.targets.First(target => target.sourceNodePath == FacePartPaths[2]);
                    RigTargetDefinition rightEyebrowTarget = rig.targets.First(target => target.sourceNodePath == FacePartPaths[3]);
                    List<MirrorPair> mirrorPairList = rig.mirrorPairs != null
                        ? rig.mirrorPairs.ToList()
                        : new List<MirrorPair>();
                    mirrorPairList.Add(new MirrorPair
                    {
                        leftTargetId = GetTargetStableId(leftEyebrowTarget),
                        rightTargetId = GetTargetStableId(rightEyebrowTarget),
                    });
                    rig.mirrorPairs = mirrorPairList.ToArray();
                }

                EditorUtility.SetDirty(rig);
                AssetDatabase.SaveAssets();

                int addedAuthoringCount = 0;
                for (int partIndex = 0; partIndex < FacePartPaths.Length; partIndex++)
                {
                    string nodePath = FacePartPaths[partIndex];
                    Transform partTransform = prefabRoot.transform.Find(nodePath);
                    if (partTransform == null)
                    {
                        continue;
                    }

                    RigTargetDefinition matchingTarget = rig.targets.FirstOrDefault(target => target.sourceNodePath == nodePath);
                    if (matchingTarget == null)
                    {
                        continue;
                    }

                    RigTargetAuthoring existingAuthoring = partTransform.GetComponent<RigTargetAuthoring>();
                    if (existingAuthoring == null)
                    {
                        existingAuthoring = partTransform.gameObject.AddComponent<RigTargetAuthoring>();
                        addedAuthoringCount++;
                    }

                    existingAuthoring.rig = null;
                    existingAuthoring.targetStableId = GetTargetStableId(matchingTarget);

                    int restSliceIndex = 0;
                    Renderer partRenderer = partTransform.GetComponent<Renderer>();
                    if (partRenderer != null && partRenderer.sharedMaterial != null &&
                        partRenderer.sharedMaterial.HasProperty("_ImageIndex"))
                    {
                        restSliceIndex = Mathf.RoundToInt(partRenderer.sharedMaterial.GetFloat("_ImageIndex"));
                    }
                    existingAuthoring.restSliceIndex = restSliceIndex;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);

                return "AddFaceTargets: rig targets now " + rig.targets.Count + " (+" + addedTargetCount +
                       "), RigTargetAuthoring added to " + addedAuthoringCount + " new part(s).";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 P6 - Author Idle Clip")]
        public static string AuthorIdleClip()
        {
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ClipAsset idleClip = GetOrCreateClip(clipSet, "Idle");
            if (idleClip == null)
            {
                return "FAILED: could not create/find Idle clip.";
            }

            idleClip.duration = 4.0f;
            idleClip.defaultLoop = LoopMode.Loop;
            idleClip.frameRate = 30f;
            idleClip.defaultBlendIn = 0.25f;
            idleClip.defaultBlendOut = 0.25f;
            idleClip.transformTracks.Clear();

            // (tagName, ±degrees at t=.5, invert sign) — all Rotation-only tracks, keys at 0/.5/1.
            AddSwayTrack(idleClip, TargetTags.Torso, 2f, false);
            AddSwayTrack(idleClip, TargetTags.Neck, 1f, true);
            AddSwayTrack(idleClip, TargetTags.Head, 1.5f, false);
            AddSwayTrack(idleClip, TargetTags.UpperLeftArm, 2f, false);
            AddSwayTrack(idleClip, TargetTags.UpperRightArm, 2f, false);
            AddSwayTrack(idleClip, TargetTags.LowerLeftArm, 3f, false);
            AddSwayTrack(idleClip, TargetTags.LowerRightArm, 3f, false);

            TransformTrack pelvisDipTrack = new TransformTrack
            {
                tagId = TargetTags.Pelvis,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.PositionXY,
            };
            pelvisDipTrack.keys.Add(MakeTransformKey(0f, float3.zero, float3.zero, Interpolation.EaseInOut));
            pelvisDipTrack.keys.Add(MakeTransformKey(0.5f, new float3(0f, -0.01f, 0f), float3.zero, Interpolation.EaseInOut));
            pelvisDipTrack.keys.Add(MakeTransformKey(1f, float3.zero, float3.zero, Interpolation.EaseInOut));
            idleClip.transformTracks.Add(pelvisDipTrack);

            EditorUtility.SetDirty(idleClip);
            AssetDatabase.SaveAssets();
            return "AuthorIdleClip: 'Idle' has " + idleClip.transformTracks.Count + " transform track(s), duration " + idleClip.duration + "s.";
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 P6 - Author Attack Clip")]
        public static string AuthorAttackClip()
        {
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ClipAsset attackClip = GetOrCreateClip(clipSet, "MeleeContinuous");
            if (attackClip == null)
            {
                return "FAILED: could not create/find MeleeContinuous clip.";
            }

            attackClip.duration = 0.5f;
            attackClip.defaultLoop = LoopMode.Once;
            attackClip.frameRate = 30f;
            attackClip.defaultBlendIn = 0.05f;
            attackClip.defaultBlendOut = 0.1f;
            attackClip.transformTracks.Clear();
            attackClip.events.Clear();

            AddAttackArmTrack(attackClip, TargetTags.UpperRightArm, -40f, 70f);
            AddAttackArmTrack(attackClip, TargetTags.LowerRightArm, -60f, 10f);
            AddAttackArmTrack(attackClip, TargetTags.RightHand, 0f, 0f);
            AddAttackArmTrack(attackClip, TargetTags.Torso, -8f, 10f);
            AddAttackArmTrack(attackClip, TargetTags.UpperLeftArm, 0f, 0f);

            attackClip.events.Add(new EventMarker
            {
                normalizedTime = 0.35f,
                eventKey = AnimEvents.Attack,
                windowSeconds = 0f,
            });

            EditorUtility.SetDirty(attackClip);
            AssetDatabase.SaveAssets();
            return "AuthorAttackClip: 'MeleeContinuous' has " + attackClip.transformTracks.Count +
                   " transform track(s), " + attackClip.events.Count + " event(s).";
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 P6 - Author Blink Clip")]
        public static string AuthorBlinkClip()
        {
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ClipAsset blinkClip = GetOrCreateClip(clipSet, "Blink");
            if (blinkClip == null)
            {
                return "FAILED: could not create/find Blink clip.";
            }

            blinkClip.duration = 5.0f;
            blinkClip.defaultLoop = LoopMode.Loop;
            blinkClip.frameRate = 30f;
            blinkClip.spriteTracks.Clear();

            uint eyesTagId = MintOrReuseTargetTag("Eyes");
            SpriteTrack eyesSpriteTrack = new SpriteTrack
            {
                tagId = eyesTagId,
                mode = SpriteFrameMode.Slice,
                sliceSpace = SpriteSliceSpace.Absolute,
            };
            int[] closingSlices = { -1, 11, 9, 7, 1, 7, 9, 11, -1 };
            float[] closingTimes = { 0f, 0.02f, 0.04f, 0.06f, 0.08f, 0.94f, 0.96f, 0.98f, 1f };
            for (int keyIndex = 0; keyIndex < closingSlices.Length; keyIndex++)
            {
                eyesSpriteTrack.keys.Add(new SpriteKey
                {
                    normalizedTime = closingTimes[keyIndex],
                    sliceIndex = closingSlices[keyIndex],
                    indexMode = SpriteIndexMode.Absolute,
                });
            }
            blinkClip.spriteTracks.Add(eyesSpriteTrack);

            uint leftEyebrowTagId = MintOrReuseTargetTag("LeftEyebrow");
            uint rightEyebrowTagId = MintOrReuseTargetTag("RightEyebrow");
            blinkClip.transformTracks.RemoveAll(track => track.tagId == leftEyebrowTagId || track.tagId == rightEyebrowTagId);
            AddEyebrowBobTrack(blinkClip, leftEyebrowTagId);
            AddEyebrowBobTrack(blinkClip, rightEyebrowTagId);

            EditorUtility.SetDirty(blinkClip);
            AssetDatabase.SaveAssets();
            return "AuthorBlinkClip: 'Blink' has " + blinkClip.spriteTracks.Count + " sprite track(s), " +
                   blinkClip.transformTracks.Count + " transform track(s).";
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 P6 - Author DeathFace Clip")]
        public static string AuthorDeathFaceClip()
        {
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ClipAsset deathFaceClip = GetOrCreateClip(clipSet, "DeathFace");
            if (deathFaceClip == null)
            {
                return "FAILED: could not create/find DeathFace clip.";
            }

            deathFaceClip.duration = 0.2f;
            deathFaceClip.defaultLoop = LoopMode.Loop;
            deathFaceClip.frameRate = 30f;
            deathFaceClip.spriteTracks.Clear();

            uint eyesTagId = MintOrReuseTargetTag("Eyes");
            SpriteTrack closedEyesTrack = new SpriteTrack
            {
                tagId = eyesTagId,
                mode = SpriteFrameMode.Slice,
                sliceSpace = SpriteSliceSpace.Absolute,
            };
            closedEyesTrack.keys.Add(new SpriteKey { normalizedTime = 0f, sliceIndex = 1, indexMode = SpriteIndexMode.Absolute });
            closedEyesTrack.keys.Add(new SpriteKey { normalizedTime = 1f, sliceIndex = 1, indexMode = SpriteIndexMode.Absolute });
            deathFaceClip.spriteTracks.Add(closedEyesTrack);

            EditorUtility.SetDirty(deathFaceClip);
            AssetDatabase.SaveAssets();
            return "AuthorDeathFaceClip: 'DeathFace' holds slice 1 on Eyes for " + deathFaceClip.duration + "s.";
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 P7 - Author Profile And Wire Units")]
        public static string AuthorProfileAndWireUnits()
        {
            string[] animationNamesToMint = { "Idle", "Walk", "MeleeContinuous", "Death", "Resurrection", "DeathFace", "ResurrectionFace", "Blink" };
            Dictionary<string, uint> animationKeyByName = new Dictionary<string, uint>();
            AnimationNameRegistry animationNameRegistry = VocabularyRegistryProvider.AnimationNames;
            foreach (string animationName in animationNamesToMint)
            {
                animationKeyByName[animationName] = MintOrReuseAnimationName(animationName);
            }
            RegenerateAnimationNamesConstants();

            RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(RigPath);
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ClipAsset idleClip = FindClip(clipSet, "Idle");
            ClipAsset walkClip = FindClip(clipSet, "Walk");
            ClipAsset attackClip = FindClip(clipSet, "MeleeContinuous");
            ClipAsset deathFaceClip = FindClip(clipSet, "DeathFace");
            ClipAsset blinkClip = FindClip(clipSet, "Blink");

            ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(ProfilePath);
            bool isNewProfile = profile == null;
            if (isNewProfile)
            {
                profile = ScriptableObject.CreateInstance<ActorProfileAsset>();
            }

            profile.rig = rig;
            profile.clipSets = new List<ClipSetAsset> { clipSet };
            profile.turnDirections = AnimationDirections.Six;
            profile.EnsureBookends();

            ActorLayerDefinition baseLayer = FindOrCreateLayer(profile, "Base");
            baseLayer.defaultActive = true;
            ActorAnimationDefinition idleAnimation = FindOrCreateAnimation(baseLayer, animationKeyByName["Idle"]);
            idleAnimation.hasDirections = true;
            idleAnimation.directionSlots.southEast = idleClip;
            idleAnimation.directionSlots.targetDirections = AnimationDirections.Two;
            idleAnimation.loop = LoopMode.Loop;
            baseLayer.startingAnimationKey = idleAnimation.animationKey;

            ActorAnimationDefinition walkAnimation = FindOrCreateAnimation(baseLayer, animationKeyByName["Walk"]);
            walkAnimation.hasDirections = true;
            walkAnimation.directionSlots.southEast = walkClip;
            walkAnimation.directionSlots.targetDirections = AnimationDirections.Two;
            walkAnimation.loop = LoopMode.Loop;

            ActorLayerDefinition actionLayer = FindOrCreateLayer(profile, "Action");
            ActorAnimationDefinition attackAnimation = FindOrCreateAnimation(actionLayer, animationKeyByName["MeleeContinuous"]);
            attackAnimation.hasDirections = true;
            attackAnimation.directionSlots.southEast = attackClip;
            attackAnimation.directionSlots.targetDirections = AnimationDirections.Two;
            attackAnimation.loop = LoopMode.Once;

            ActorAnimationDefinition deathAnimation = FindOrCreateAnimation(actionLayer, animationKeyByName["Death"]);
            deathAnimation.hasDirections = false;
            deathAnimation.clip = null;
            deathAnimation.ragdollTrigger = RagdollTrigger.Start;
            deathAnimation.ragdollAtEventKey = 0;

            ActorAnimationDefinition resurrectionAnimation = FindOrCreateAnimation(actionLayer, animationKeyByName["Resurrection"]);
            resurrectionAnimation.hasDirections = false;
            resurrectionAnimation.clip = null;
            resurrectionAnimation.ragdollTrigger = RagdollTrigger.Stop;
            resurrectionAnimation.ragdollAtEventKey = 0;

            // Face stays an empty slot: the Eyes layer composites above it, so a death face put on
            // Face would be overridden by the blink. DeathFace lives on Eyes and replaces Blink there;
            // ResurrectionFace is Blink again, so a revived unit resumes blinking.
            ActorLayerDefinition faceLayer = FindOrCreateLayer(profile, "Face");
            faceLayer.animations.RemoveAll(animation => animation.animationKey == animationKeyByName["DeathFace"]);

            ActorLayerDefinition eyesLayer = FindOrCreateLayer(profile, "Eyes");
            eyesLayer.defaultActive = true;
            ActorAnimationDefinition blinkAnimation = FindOrCreateAnimation(eyesLayer, animationKeyByName["Blink"]);
            blinkAnimation.hasDirections = false;
            blinkAnimation.clip = blinkClip;
            blinkAnimation.loop = LoopMode.Loop;
            eyesLayer.startingAnimationKey = blinkAnimation.animationKey;

            ActorAnimationDefinition deathFaceAnimation = FindOrCreateAnimation(eyesLayer, animationKeyByName["DeathFace"]);
            deathFaceAnimation.hasDirections = false;
            deathFaceAnimation.clip = deathFaceClip;
            deathFaceAnimation.loop = LoopMode.Loop;

            ActorAnimationDefinition resurrectionFaceAnimation = FindOrCreateAnimation(eyesLayer, animationKeyByName["ResurrectionFace"]);
            resurrectionFaceAnimation.hasDirections = false;
            resurrectionFaceAnimation.clip = blinkClip;
            resurrectionFaceAnimation.loop = LoopMode.Loop;

            FindOrCreateLayer(profile, "Mouth");
            profile.EnsureStableIds();

            if (isNewProfile)
            {
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ActorAuthoring actorAuthoring = prefabRoot.GetComponent<ActorAuthoring>();
                if (actorAuthoring != null)
                {
                    actorAuthoring.profile = profile;
                }
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            SetUnitActorProfile(MaleCitizenUnitPath, profile);
            SetUnitActorProfile(RotterUnitPath, profile);

            DirectionSetAsset walkDirectionSet = AssetDatabase.LoadAssetAtPath<DirectionSetAsset>(WalkDirectionSetPath);
            if (walkDirectionSet == null)
            {
                walkDirectionSet = ScriptableObject.CreateInstance<DirectionSetAsset>();
                walkDirectionSet.slots.southEast = walkClip;
                walkDirectionSet.slots.targetDirections = AnimationDirections.Two;
                AssetDatabase.CreateAsset(walkDirectionSet, WalkDirectionSetPath);
            }
            else
            {
                walkDirectionSet.slots.southEast = walkClip;
                walkDirectionSet.slots.targetDirections = AnimationDirections.Two;
                EditorUtility.SetDirty(walkDirectionSet);
            }
            AssetDatabase.SaveAssets();

            CutsceneAsset cutsceneAsset = AssetDatabase.LoadAssetAtPath<CutsceneAsset>(CutscenePath);
            if (cutsceneAsset != null && cutsceneAsset.slots.Count > 0)
            {
                cutsceneAsset.slots[0].directionSet = walkDirectionSet;
                EditorUtility.SetDirty(cutsceneAsset);
                AssetDatabase.SaveAssets();
            }

            if (AssetDatabase.LoadAssetAtPath<DirectionSetAsset>(OldWalkDirectionSetPath) != null)
            {
                AssetDatabase.DeleteAsset(OldWalkDirectionSetPath);
            }

            List<ValidationMessage> validationMessages = ActorProfileValidation.Validate(profile, animationNameRegistry);
            return "AuthorProfileAndWireUnits: profile has " + profile.layers.Count + " layer(s); validation message count = " +
                   validationMessages.Count + ".";
        }

        [MenuItem("Stitch Punk/Content Authoring/G5 - Verify")]
        public static string Verify()
        {
            RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(RigPath);
            ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(ClipSetPath);
            ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(ProfilePath);
            AnimationNameRegistry animationNameRegistry = VocabularyRegistryProvider.AnimationNames;
            TargetTagRegistry targetTagRegistry = VocabularyRegistryProvider.TargetTags;

            int rigTargetCount = rig != null ? rig.targets.Count : -1;
            int targetTagCount = targetTagRegistry != null ? ((DotsAnimationToolkit.Authoring.IVocabularyRegistry)targetTagRegistry).VocabularyEntryCount : -1;
            int clipCount = clipSet != null ? clipSet.clips.Count : -1;
            int profileLayerCount = profile != null ? profile.layers.Count : -1;
            List<ValidationMessage> validationMessages = profile != null
                ? ActorProfileValidation.Validate(profile, animationNameRegistry)
                : new List<ValidationMessage>();

            return "Verify: rig targets=" + rigTargetCount + " tags=" + targetTagCount + " clips=" + clipCount +
                   " profileLayers=" + profileLayerCount + " validationMessages=" + validationMessages.Count;
        }

        // ---- helpers ----

        private static ClipAsset GetOrCreateClip(ClipSetAsset clipSet, string clipName)
        {
            ClipAsset existingClip = FindClip(clipSet, clipName);
            if (existingClip != null)
            {
                return existingClip;
            }

            ClipAsset newClip = ClipAssetUtility.CreateClipInSet(clipSet);
            if (newClip == null)
            {
                return null;
            }
            ClipAssetUtility.RenameClip(newClip, clipName);
            return newClip;
        }

        private static ClipAsset FindClip(ClipSetAsset clipSet, string clipName)
        {
            if (clipSet == null)
            {
                return null;
            }
            return clipSet.clips.FirstOrDefault(clip => clip != null && clip.name == clipName);
        }

        private static uint MintOrReuseTargetTag(string tagName)
        {
            TargetTagRegistry registry = VocabularyRegistryProvider.TargetTags;
            for (int entryIndex = 0; entryIndex < ((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryCount; entryIndex++)
            {
                if (((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryName(entryIndex) == tagName)
                {
                    return ((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryId(entryIndex);
                }
            }
            uint mintedId = registry.CreateVocabularyEntry(tagName);
            VocabularyRegistryProvider.Persist(registry);
            return mintedId;
        }

        private static uint MintOrReuseAnimationName(string animationName)
        {
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            for (int entryIndex = 0; entryIndex < ((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryCount; entryIndex++)
            {
                if (((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryName(entryIndex) == animationName)
                {
                    return ((DotsAnimationToolkit.Authoring.IVocabularyRegistry)registry).VocabularyEntryId(entryIndex);
                }
            }
            uint mintedId = registry.CreateVocabularyEntry(animationName);
            VocabularyRegistryProvider.Persist(registry);
            return mintedId;
        }

        // Mirrors VocabularyConstantsSection.RegenerateIfConfigured (that type is a VisualElement,
        // not constructible headlessly) so TargetTags.cs is rewritten the same way the toolkit
        // itself writes it, minting the stored destination path the first time one is needed.
        private static void RegenerateTargetTagsConstants()
        {
            TargetTagRegistry registry = VocabularyRegistryProvider.TargetTags;
            RegenerateConstantsFile(registry, "TargetTags", "Target tag", "Tag");
            VocabularyRegistryProvider.Persist(registry);
        }

        private static void RegenerateAnimationNamesConstants()
        {
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            RegenerateConstantsFile(registry, "AnimNames", "Animation", "Animation");
            VocabularyRegistryProvider.Persist(registry);
        }

        private static void RegenerateConstantsFile(
            IVocabularyRegistry registry, string defaultFileName, string entryNoun, string fallbackEntryNamePrefix)
        {
            const string defaultDestinationDirectory = "Assets/Generated/DotsAnimationToolkit";
            string storedPath = registry.GeneratedConstantsPath;
            if (string.IsNullOrEmpty(storedPath))
            {
                storedPath = defaultDestinationDirectory + "/" + defaultFileName + "." + ConstantsGenerator.GeneratedFileExtension;
                registry.GeneratedConstantsPath = storedPath;
            }

            List<string> reports = new List<string>();
            string className = ConstantsGenerator.ClassNameFromFilePath(storedPath, defaultFileName);
            string generatedSource = ConstantsGenerator.BuildVocabularyConstantsSource(
                registry, className, entryNoun, fallbackEntryNamePrefix, reports);
            ConstantsGenerator.WriteGeneratedFile(storedPath, generatedSource);

            for (int reportIndex = 0; reportIndex < reports.Count; reportIndex++)
            {
                Debug.LogWarning("MaleCitizenContentAuthoring: " + reports[reportIndex]);
            }
        }

        private static uint GetTargetStableId(RigTargetDefinition targetDefinition)
        {
            return targetDefinition.Id.Value;
        }

        private static bool IsFaceTagPair(RigAsset rig, MirrorPair pair, string leftTagName, string rightTagName)
        {
            RigTargetDefinition leftTarget = rig.targets.FirstOrDefault(target => GetTargetStableId(target) == pair.leftTargetId);
            RigTargetDefinition rightTarget = rig.targets.FirstOrDefault(target => GetTargetStableId(target) == pair.rightTargetId);
            return leftTarget != null && rightTarget != null &&
                   leftTarget.sourceNodePath.EndsWith(leftTagName) && rightTarget.sourceNodePath.EndsWith(rightTagName);
        }

        private static ActorLayerDefinition FindOrCreateLayer(ActorProfileAsset profile, string displayName)
        {
            ActorLayerDefinition existingLayer = profile.layers.FirstOrDefault(layer => layer.displayName == displayName);
            if (existingLayer != null)
            {
                return existingLayer;
            }
            ActorLayerDefinition newLayer = new ActorLayerDefinition { displayName = displayName };
            // Insert before the Override bookend (always last) so bookend positions never move.
            profile.layers.Insert(profile.layers.Count - 1, newLayer);
            return newLayer;
        }

        private static ActorAnimationDefinition FindOrCreateAnimation(ActorLayerDefinition layer, uint animationKey)
        {
            ActorAnimationDefinition existingAnimation = layer.animations.FirstOrDefault(animation => animation.animationKey == animationKey);
            if (existingAnimation != null)
            {
                return existingAnimation;
            }
            ActorAnimationDefinition newAnimation = new ActorAnimationDefinition { animationKey = animationKey };
            layer.animations.Add(newAnimation);
            return newAnimation;
        }

        private static void SetUnitActorProfile(string unitAssetPath, ActorProfileAsset profile)
        {
            UnitSO unitSO = AssetDatabase.LoadAssetAtPath<UnitSO>(unitAssetPath);
            if (unitSO == null)
            {
                Debug.LogWarning("MaleCitizenContentAuthoring: no UnitSO at '" + unitAssetPath + "'.");
                return;
            }
            unitSO.actorProfile = profile;
            EditorUtility.SetDirty(unitSO);
        }

        private static void AddSwayTrack(ClipAsset clip, uint tagId, float degrees, bool invert)
        {
            clip.transformTracks.RemoveAll(track => track.tagId == tagId);
            float signedDegrees = invert ? -degrees : degrees;
            TransformTrack swayTrack = new TransformTrack
            {
                tagId = tagId,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.Rotation,
            };
            swayTrack.keys.Add(MakeTransformKey(0f, float3.zero, new float3(0f, 0f, signedDegrees), Interpolation.EaseInOut));
            swayTrack.keys.Add(MakeTransformKey(0.5f, float3.zero, new float3(0f, 0f, -signedDegrees), Interpolation.EaseInOut));
            swayTrack.keys.Add(MakeTransformKey(1f, float3.zero, new float3(0f, 0f, signedDegrees), Interpolation.EaseInOut));
            clip.transformTracks.Add(swayTrack);
        }

        private static void AddAttackArmTrack(ClipAsset clip, uint tagId, float windUpDegrees, float strikeDegrees)
        {
            clip.transformTracks.RemoveAll(track => track.tagId == tagId);
            TransformTrack armTrack = new TransformTrack
            {
                tagId = tagId,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.Rotation,
            };
            armTrack.keys.Add(MakeTransformKey(0f, float3.zero, float3.zero, Interpolation.Linear));
            armTrack.keys.Add(MakeTransformKey(0.2f, float3.zero, new float3(0f, 0f, windUpDegrees), Interpolation.Linear));
            armTrack.keys.Add(MakeTransformKey(0.35f, float3.zero, new float3(0f, 0f, strikeDegrees), Interpolation.EaseOut));
            armTrack.keys.Add(MakeTransformKey(1f, float3.zero, float3.zero, Interpolation.EaseOut));
            clip.transformTracks.Add(armTrack);
        }

        private static void AddEyebrowBobTrack(ClipAsset clip, uint tagId)
        {
            TransformTrack eyebrowTrack = new TransformTrack
            {
                tagId = tagId,
                blendOp = TrackBlendOp.Override,
                channels = AnimatedChannels.PositionXY,
            };
            eyebrowTrack.keys.Add(MakeTransformKey(0f, new float3(0f, -0.005f, 0f), float3.zero, Interpolation.EaseInOut));
            eyebrowTrack.keys.Add(MakeTransformKey(0.5f, new float3(0f, 0.01f, 0f), float3.zero, Interpolation.EaseInOut));
            eyebrowTrack.keys.Add(MakeTransformKey(1f, new float3(0f, -0.005f, 0f), float3.zero, Interpolation.EaseInOut));
            clip.transformTracks.Add(eyebrowTrack);
        }

        private static TransformKey MakeTransformKey(float normalizedTime, float3 position, float3 rotationDegrees, Interpolation interpolation)
        {
            return new TransformKey
            {
                normalizedTime = normalizedTime,
                position = position,
                rotation = rotationDegrees,
                scale = new float3(1f, 1f, 1f),
                interpolation = interpolation,
            };
        }
    }
}
