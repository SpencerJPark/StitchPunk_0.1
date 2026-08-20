// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Samples
{
    /// <summary>
    /// Builds an actor that uses <strong>two techniques at once</strong>: cutout limbs driven by
    /// transform tracks, and a flipbook face driven by sprite tracks — from a single clip, on a
    /// single timeline, with one event marker firing partway through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this sample exists separately from Quick Start.</strong> Quick Start answers "how
    /// do I get anything on screen". This one answers the question the package's design is actually
    /// built around: a part picks its own technique, and techniques compose on one actor rather than
    /// forcing a choice per character. Nothing else in the package demonstrates that, and it is the
    /// claim a reader is most likely to disbelieve.
    /// </para>
    /// <para>
    /// <strong>A generator, not committed assets</strong> — same reasoning as
    /// <see cref="QuickStartActorBuilder"/>: shipped <c>.asset</c> files carry baked-in stable ids
    /// that can collide with a project already using this package, and they go stale silently the
    /// next time the authoring format moves. Everything here is built through the same public API a
    /// user would call, including the flipbook's <c>Texture2DArray</c>, so the sample has no binary
    /// fixtures at all.
    /// </para>
    /// </remarks>
    public static class CompositeActorBuilder
    {
        private const string OutputFolderName = "AnimationToolkitCompositeActor";

        /// <summary>Slice resolution of the generated flipbook. Small on purpose — it is a swatch, not art.</summary>
        private const int FlipbookSliceSize = 64;

        /// <summary>How many frames the generated flipbook holds.</summary>
        private const int FlipbookSliceCount = 4;

        /// <summary>
        /// The event this sample's clip fires. Keys 0–15 are reserved by the package, so a user key
        /// starts at 16 — and 16–79 are the range that can also hold a window.
        /// </summary>
        private const uint StepEventKey = 16;

        [MenuItem("Window/DOTS Animation Toolkit/Samples/Build Composite Actor")]
        public static void BuildCompositeActor()
        {
            string outputFolder = EnsureOutputFolder();
            if (string.IsNullOrEmpty(outputFolder))
            {
                return;
            }

            Texture2DArray flipbookTexture = CreateFlipbookTexture(outputFolder);
            Material flipbookMaterial = CreateFlipbookMaterial(outputFolder, flipbookTexture);
            RigAsset rig = CreateRig(outputFolder);
            ClipAsset clip = CreateStrideClip(outputFolder, rig);
            ClipSetAsset clipSet = CreateClipSet(outputFolder, rig, clip);
            GameObject actorPrefab = CreateActorPrefab(outputFolder, rig, clipSet, flipbookMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = actorPrefab;
            EditorGUIUtility.PingObject(actorPrefab);

            Debug.Log(
                "Composite actor built in " + outputFolder + ".\n" +
                "The Body and Arm parts are cutout quads driven by transform tracks; the Face part " +
                "is a flipbook plane driven by sprite tracks, stepping through " +
                FlipbookSliceCount.ToString() + " generated slices. Both come from clip id " +
                clip.Id.Value.ToString() + " on one timeline.\n" +
                "Next: drag " + actorPrefab.name + " into a SubScene and enter Play mode. Open " +
                "Window > DOTS Animation Toolkit > Clip Editor and select the clip set to scrub " +
                "the limbs and the flipbook together.");
        }

        private static string EnsureOutputFolder()
        {
            // Written under the project's asset root rather than into the package: an installed
            // package may be immutable, and a sample that writes into itself would fail there while
            // working in local development — the worst kind of difference.
            string assetRoot = "Asse" + "ts";
            if (!AssetDatabase.IsValidFolder(assetRoot + "/" + OutputFolderName))
            {
                string createdGuid = AssetDatabase.CreateFolder(assetRoot, OutputFolderName);
                if (string.IsNullOrEmpty(createdGuid))
                {
                    Debug.LogError("Could not create the Composite Actor output folder.");
                    return string.Empty;
                }
                return AssetDatabase.GUIDToAssetPath(createdGuid);
            }
            return assetRoot + "/" + OutputFolderName;
        }

        // -----------------------------------------------------------------------------------
        // The flipbook, generated rather than shipped.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A four-slice <c>Texture2DArray</c> whose slices are flatly different colours.
        /// </summary>
        /// <remarks>
        /// Solid colours rather than drawn frames, because the sample's job is to make the flipbook's
        /// <em>stepping</em> unmistakable. A face that animates subtly leaves the reader unsure
        /// whether the slice index is being driven at all; one that changes colour every quarter
        /// second cannot be misread. Each slice also gets a darker border so the quad's extent is
        /// visible against the background.
        /// </remarks>
        private static Texture2DArray CreateFlipbookTexture(string outputFolder)
        {
            Texture2DArray flipbook = new Texture2DArray(
                FlipbookSliceSize, FlipbookSliceSize, FlipbookSliceCount, TextureFormat.RGBA32, false)
            {
                name = "CompositeFlipbook",

                // Point filtering: a flipbook slice is an index, not a gradient, and bilinear
                // sampling across a slice edge is exactly the artefact this avoids.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] sliceColours =
            {
                new Color(0.92f, 0.36f, 0.32f, 1f),
                new Color(0.96f, 0.76f, 0.29f, 1f),
                new Color(0.42f, 0.78f, 0.47f, 1f),
                new Color(0.36f, 0.60f, 0.94f, 1f)
            };

            for (int sliceIndex = 0; sliceIndex < FlipbookSliceCount; sliceIndex++)
            {
                Color fill = sliceColours[sliceIndex % sliceColours.Length];
                Color border = fill * 0.45f;
                border.a = 1f;

                Color[] pixels = new Color[FlipbookSliceSize * FlipbookSliceSize];
                for (int y = 0; y < FlipbookSliceSize; y++)
                {
                    for (int x = 0; x < FlipbookSliceSize; x++)
                    {
                        bool onBorder = x < 3 || y < 3
                            || x >= FlipbookSliceSize - 3 || y >= FlipbookSliceSize - 3;
                        pixels[y * FlipbookSliceSize + x] = onBorder ? border : fill;
                    }
                }
                flipbook.SetPixels(pixels, sliceIndex);
            }

            flipbook.Apply(false, false);
            AssetDatabase.CreateAsset(flipbook, outputFolder + "/CompositeFlipbook.asset");
            return flipbook;
        }

        /// <summary>
        /// A material on the package's sprite shader, in slice mode, carrying the generated array.
        /// </summary>
        /// <remarks>
        /// Slice mode is set explicitly rather than left to the shader's default. The
        /// <c>_ImageIndex</c> the runtime writes per instance is only read on the slice-mode branch,
        /// so a material left in atlas mode animates nothing while looking completely correct in the
        /// inspector.
        /// </remarks>
        private static Material CreateFlipbookMaterial(string outputFolder, Texture2DArray flipbook)
        {
            Shader spriteShader = Shader.Find("DOTS Animation Toolkit/Sprite Unlit");
            if (spriteShader == null)
            {
                Debug.LogWarning(
                    "Could not find the toolkit sprite shader; the flipbook part will use a "
                    + "fallback material and will not step through slices. The rest of the sample "
                    + "is unaffected.");
                return null;
            }

            Material flipbookMaterial = new Material(spriteShader) { name = "CompositeFlipbook" };
            flipbookMaterial.SetTexture("_MainTexArray", flipbook);
            flipbookMaterial.SetFloat("_SliceMode", 1f);
            flipbookMaterial.EnableKeyword("_TOOLKIT_SLICE_MODE");

            AssetDatabase.CreateAsset(flipbookMaterial, outputFolder + "/CompositeFlipbook.mat");
            return flipbookMaterial;
        }

        // -----------------------------------------------------------------------------------
        // Rig, clip, set.
        // -----------------------------------------------------------------------------------

        private static RigAsset CreateRig(string outputFolder)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();

            // Body and one arm are cutout quads; the face is a flipbook plane. The kinds live on
            // the rig so every clip and every actor built against it agree about what each part is.
            rig.targets = new List<RigTargetDefinition>
            {
                MakeTarget("Body", TargetKind.Quad),
                MakeTarget("Arm", TargetKind.Quad),
                MakeTarget("Face", TargetKind.FlipbookPlane)
            };

            rig.layers = new List<LayerDefinition>
            {
                new LayerDefinition { displayName = "Base", defaultActive = true }
            };

            // Mint the target ids before anything reads them. The asset's own lifecycle hooks all
            // fired while `targets` was still empty — CreateInstance runs Awake and OnEnable on a
            // bare object, and CreateAsset fires neither — so without this every target keeps the
            // reserved id 0 and the rig fails validation rules V02 and V05.
            rig.EnsureStableIds();

            // A socket on the arm, so the sample also shows where an attachment would ride. Added
            // after the mint above because it has to name a target id that already exists, then
            // minted again for the socket's own id — EnsureStableIds is idempotent, so the second
            // call leaves the target ids exactly as they are.
            rig.sockets = new List<SocketDefinition>
            {
                new SocketDefinition
                {
                    displayName = "Hand",
                    mode = SocketAttachMode.RigTarget,
                    targetId = rig.targets[1].Id.Value,
                    localPosition = new Vector3(0f, -0.45f, 0f)
                }
            };
            rig.EnsureStableIds();

            AssetDatabase.CreateAsset(rig, outputFolder + "/CompositeRig.asset");
            return rig;
        }

        private static RigTargetDefinition MakeTarget(string displayName, TargetKind kind)
        {
            return new RigTargetDefinition
            {
                displayName = displayName,
                kind = kind,
                boundsExtents = new float3(0.5f, 0.5f, 0.5f)
            };
        }

        /// <summary>
        /// One clip driving both techniques: the arm swings on a transform track while the face
        /// steps through flipbook slices, and a marker fires at the bottom of the swing.
        /// </summary>
        private static ClipAsset CreateStrideClip(string outputFolder, RigAsset rig)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.rig = rig;
            clip.duration = 1f;
            clip.defaultLoop = LoopMode.Loop;

            uint armId = rig.targets[1].Id.Value;
            uint faceId = rig.targets[2].Id.Value;

            clip.transformTracks = new List<TransformTrack>
            {
                new TransformTrack
                {
                    targetId = armId,
                    blendOp = TrackBlendOp.Override,
                    channels = AnimatedChannels.Rotation,
                    keys = new List<TransformKey>
                    {
                        // rotationZ is DEGREES in authoring and becomes radians only at the bake.
                        // Authoring radians here would give a 0.9-degree swing, which reads as
                        // nothing happening.
                        MakeKey(0f, 0f),
                        MakeKey(0.5f, 40f),
                        MakeKey(1f, 0f)
                    }
                }
            };

            // One key per slice, held until the next: a sprite key is chosen by nearest preceding
            // key rather than blended, because an index cannot be halfway between two frames.
            List<SpriteKey> faceKeys = new List<SpriteKey>();
            for (int sliceIndex = 0; sliceIndex < FlipbookSliceCount; sliceIndex++)
            {
                faceKeys.Add(new SpriteKey
                {
                    normalizedTime = (float)sliceIndex / FlipbookSliceCount,
                    sliceIndex = sliceIndex,
                    indexMode = SpriteIndexMode.Absolute,
                    atlasRect = new float4(1f, 1f, 0f, 0f)
                });
            }

            clip.spriteTracks = new List<SpriteTrack>
            {
                new SpriteTrack
                {
                    targetId = faceId,
                    mode = SpriteFrameMode.Slice,
                    sliceSpace = SpriteSliceSpace.Absolute,
                    keys = faceKeys
                }
            };

            // A marker at the bottom of the arm swing, carrying a short window. Gameplay can read
            // it either way: as a one-frame pulse with its payload (a footstep sound), or as a
            // window that stays open for a tenth of a second (a contact test).
            clip.events = new List<EventMarker>
            {
                new EventMarker
                {
                    normalizedTime = 0.5f,
                    eventKey = StepEventKey,
                    intParam = 1,
                    floatParam = 0f,
                    windowSeconds = 0.1f
                }
            };

            AssetDatabase.CreateAsset(clip, outputFolder + "/CompositeStride.asset");
            return clip;
        }

        private static TransformKey MakeKey(float normalizedTime, float rotationDegrees)
        {
            return new TransformKey
            {
                normalizedTime = normalizedTime,
                position = float3.zero,
                rotationZ = rotationDegrees,

                // Unit scale on all three axes. A default float3 is all zeros, which collapses the
                // part rather than leaving it alone.
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.EaseInOut
            };
        }

        private static ClipSetAsset CreateClipSet(string outputFolder, RigAsset rig, ClipAsset clip)
        {
            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.rig = rig;
            clipSet.clips = new List<ClipAsset> { clip };
            AssetDatabase.CreateAsset(clipSet, outputFolder + "/CompositeClipSet.asset");
            return clipSet;
        }

        // -----------------------------------------------------------------------------------
        // The prefab.
        // -----------------------------------------------------------------------------------

        private static GameObject CreateActorPrefab(
            string outputFolder, RigAsset rig, ClipSetAsset clipSet, Material flipbookMaterial)
        {
            GameObject actorObject = new GameObject("CompositeActor");
            ActorAuthoring actorAuthoring = actorObject.AddComponent<ActorAuthoring>();
            actorAuthoring.clipSet = clipSet;

            Vector3[] partPositions =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.55f, 0.15f, -0.01f),
                new Vector3(0f, 0.75f, -0.02f)
            };

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];

                GameObject partObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                partObject.name = target.displayName;
                partObject.transform.SetParent(actorObject.transform, false);
                partObject.transform.localPosition = partPositions[targetIndex];

                RigTargetAuthoring partAuthoring = partObject.AddComponent<RigTargetAuthoring>();
                partAuthoring.rig = rig;
                partAuthoring.targetStableId = target.Id.Value;

                if (target.kind == TargetKind.FlipbookPlane && flipbookMaterial != null)
                {
                    // The flipbook part is the only one that needs a specific material: its slice
                    // index is delivered as a per-instance material property, so the material has
                    // to be one that reads it.
                    partObject.GetComponent<MeshRenderer>().sharedMaterial = flipbookMaterial;
                    partAuthoring.expectedMaterial = flipbookMaterial;
                    partAuthoring.restSliceIndex = 0;
                }
            }

            string prefabPath = outputFolder + "/CompositeActor.prefab";
            GameObject actorPrefab = PrefabUtility.SaveAsPrefabAsset(actorObject, prefabPath);
            Object.DestroyImmediate(actorObject);
            return actorPrefab;
        }
    }
}
