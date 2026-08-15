// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using StitchPunk.AnimationToolkit;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkitMigration.Editor
{
    /// <summary>
    /// Converts the host game's <c>AnimationClipSO</c> content into DOTS Animation Toolkit assets —
    /// step 1 of the §13.2 cutover order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Non-destructive by construction.</strong> Nothing here writes to, moves, or deletes a
    /// single host asset: it reads the old SOs and produces new ones beside them. The old pipeline
    /// keeps running untouched, which is what lets both be compared side by side (§13.2 step 2)
    /// before anything is rewritten.
    /// </para>
    /// <para>
    /// <strong>Re-runnable.</strong> Every generated asset is replaced wholesale on each run, so the
    /// converter can be re-run after fixing host data without leaving stale output behind. The one
    /// thing it deliberately does <em>not</em> preserve across runs is stable ids — a re-run mints
    /// fresh ones, so re-running after content has been authored against the generated constants
    /// would invalidate them. That is why the generated constants class carries the ids as literals:
    /// the moment you build on the output, stop re-running this.
    /// </para>
    /// </remarks>
    public static class HostClipConverter
    {
        private const string SourceFolder = "Assets/ScriptableObjects/Animations";
        private const string OutputFolder = "Assets/AnimationToolkitMigration/Generated";
        private const string GeneratedCodeFolder = "Assets/AnimationToolkitMigration/Generated/Code";

        /// <summary>
        /// Blend seconds given to a clip whose host <c>allowBlendIn/Out</c> was true.
        /// </summary>
        /// <remarks>
        /// The host stored these as booleans, so the real duration was never authored — the audit
        /// found the fields baked and then never read at all. §12 R1 recommends crossfades of 0.25 s
        /// or less between related clips; 0.15 s sits inside that and is short enough that a wrong
        /// guess reads as a slightly soft transition rather than a visible drift. Every converted
        /// clip carries it, so re-tuning is a multi-select edit in the inspector.
        /// </remarks>
        private const float DefaultBlendSeconds = 0.15f;

        /// <summary>
        /// The one host clip deliberately not converted (owner decision, 2026-08-06).
        /// </summary>
        /// <remarks>
        /// <c>Male_SouthWest</c> was authored for the <c>Direction</c> layer, which amendment A37
        /// removes: it holds small per-part <em>offsets</em> meant to composite over a
        /// direction-agnostic <c>Walk</c>, not a walk cycle of its own. Under the new model a
        /// direction clip is a complete locomotion cycle on the Base layer, so converting this one
        /// would produce an asset that puts parts in offset positions with no underlying motion —
        /// not corrupt, just meaningless, and playable by accident. It stays where it is as
        /// reference material while the 8-direction set is authored fresh.
        /// </remarks>
        private const string SupersededDirectionClipName = "Male_SouthWest";

        [MenuItem("Tools/DOTS Animation Toolkit/Migration/Convert Host Clips")]
        public static void ConvertHostClips()
        {
            EnsureFolder("Assets", "AnimationToolkitMigration");
            EnsureFolder("Assets/AnimationToolkitMigration", "Generated");
            EnsureFolder(OutputFolder, "Code");

            List<AnimationClipSO> hostClips = LoadHostClips(out List<string> skipped);
            if (hostClips.Count == 0)
            {
                Debug.LogError("[HostClipConverter] No AnimationClipSO assets found under " + SourceFolder);
                return;
            }

            RigAsset rig = BuildHumanoidRig(out Dictionary<AnimationTarget, uint> targetIdByEnum);
            List<ClipAsset> converted = new List<ClipAsset>();
            List<SoundType> soundTypesUsed = new List<SoundType>();

            for (int clipIndex = 0; clipIndex < hostClips.Count; clipIndex++)
            {
                converted.Add(ConvertClip(hostClips[clipIndex], rig, targetIdByEnum, soundTypesUsed));
            }

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.rig = rig;
            clipSet.clips.AddRange(converted);
            CreateOrReplaceAsset(clipSet, OutputFolder + "/HostClipSet.asset");

            WriteClipConstants(converted);
            WriteSoundEventKeys(soundTypesUsed);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReportValidation(rig, converted, clipSet, skipped);
        }

        // -------------------------------------------------------------------------------------
        // Rig
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the Humanoid rig from the host's <c>AnimationTarget</c> and
        /// <c>AnimationLayerType</c> enums.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The <c>Direction</c> layer is dropped</strong> (amendment A37). It existed to
        /// composite a facing offset over a direction-agnostic base clip, and that model was replaced
        /// by per-direction locomotion clips plus a <c>SpriteViewOffset</c> component. Carrying an
        /// empty layer forward would leave a slot that looks authored-against and never composites
        /// anything. §13.1's "7 layers → 7" therefore becomes 6.
        /// </para>
        /// <para>
        /// <c>framesPerVariant</c> is left at 1 on every target (owner decision, 2026-08-06), which
        /// makes facing inert: no target is baked a <c>SpriteViewOffset</c> and no character can show
        /// another's art until the real texture strides are filled in per target.
        /// </para>
        /// </remarks>
        private static RigAsset BuildHumanoidRig(out Dictionary<AnimationTarget, uint> targetIdByEnum)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();

            Array targetValues = Enum.GetValues(typeof(AnimationTarget));
            for (int valueIndex = 0; valueIndex < targetValues.Length; valueIndex++)
            {
                AnimationTarget hostTarget = (AnimationTarget)targetValues.GetValue(valueIndex);
                RigTargetDefinition targetDefinition = new RigTargetDefinition();
                targetDefinition.displayName = hostTarget.ToString();

                // FlipbookPlane, not Quad. Every host part renders from a Texture2DArray addressed
                // by _ImageIndex — that is what the whole ImageIndex/ImageIndexOverride path exists
                // for — so every target is a flipbook.
                //
                // Getting this wrong is silent and total: TargetKind.Quad is transform-only, so
                // RigTargetBaker gives the part no SpriteSliceProperty, SpriteMaterialSystem never
                // matches it, and a sampled slice change lands in TargetPose.sliceIndex and stops
                // there. The rig animates its transforms perfectly and never changes a single frame.
                targetDefinition.kind = TargetKind.FlipbookPlane;
                targetDefinition.boundsExtents = new float3(0.5f, 0.5f, 0.1f);
                targetDefinition.framesPerVariant = 1;

                // Every host target is 2D cutout art on a rig that turns, so all of them face.
                // framesPerVariant stays at 1 (owner decision) which keeps ALT VIEWS inert until the
                // real texture strides are known — but mirroring needs no block, so opting in here
                // is what makes a flip expressible at all.
                targetDefinition.facesDirection = true;
                rig.targets.Add(targetDefinition);
            }

            Array layerValues = Enum.GetValues(typeof(AnimationLayerType));
            for (int valueIndex = 0; valueIndex < layerValues.Length; valueIndex++)
            {
                AnimationLayerType hostLayer = (AnimationLayerType)layerValues.GetValue(valueIndex);
                if (hostLayer == AnimationLayerType.Direction)
                {
                    continue;
                }
                LayerDefinition layerDefinition = new LayerDefinition();
                layerDefinition.displayName = hostLayer.ToString();
                layerDefinition.defaultActive = hostLayer == AnimationLayerType.Base;
                rig.layers.Add(layerDefinition);
            }

            CreateOrReplaceAsset(rig, OutputFolder + "/HumanoidRig.asset");
            MintTargetIds(rig);

            targetIdByEnum = new Dictionary<AnimationTarget, uint>();
            for (int valueIndex = 0; valueIndex < targetValues.Length; valueIndex++)
            {
                AnimationTarget hostTarget = (AnimationTarget)targetValues.GetValue(valueIndex);
                targetIdByEnum[hostTarget] = rig.targets[valueIndex].Id.Value;
            }
            return rig;
        }

        /// <summary>
        /// Fills the internal per-target stable ids through <see cref="SerializedObject"/>.
        /// </summary>
        /// <remarks>
        /// <c>RigAsset</c> mints its own id in <c>Awake</c>, but rows appended afterwards keep the
        /// reserved 0 and the method that fills them is package-internal. Writing the serialized
        /// field is the sanctioned editor route, and <c>StableIdUtility</c> is public precisely so a
        /// tool can mint a conformant id rather than invent one.
        /// </remarks>
        private static void MintTargetIds(RigAsset rig)
        {
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
        }

        // -------------------------------------------------------------------------------------
        // Clips
        // -------------------------------------------------------------------------------------

        private static ClipAsset ConvertClip(
            AnimationClipSO hostClip,
            RigAsset rig,
            Dictionary<AnimationTarget, uint> targetIdByEnum,
            List<SoundType> soundTypesUsed)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.rig = rig;
            clip.duration = math.max(ClipAsset.MinimumDuration, hostClip.duration);
            clip.defaultLoop = hostClip.looping ? LoopMode.Loop : LoopMode.Once;
            clip.defaultBlendIn = hostClip.allowBlendIn ? DefaultBlendSeconds : 0f;
            clip.defaultBlendOut = hostClip.allowBlendOut ? DefaultBlendSeconds : 0f;

            for (int trackIndex = 0; trackIndex < hostClip.partTracks.Count; trackIndex++)
            {
                AnimationClipSO.PartTrack hostTrack = hostClip.partTracks[trackIndex];
                if (hostTrack == null || hostTrack.keyframes == null || hostTrack.keyframes.Count == 0)
                {
                    continue;
                }
                if (!targetIdByEnum.TryGetValue(hostTrack.animationTarget, out uint targetId))
                {
                    continue;
                }

                AnimatedChannels channels = ToChannels(hostTrack.animatedProperties);
                if (channels != AnimatedChannels.None)
                {
                    clip.transformTracks.Add(BuildTransformTrack(hostTrack, targetId, channels));
                }

                // A host track carries transform and image data together; the package splits them,
                // so one PartTrack can become two tracks.
                if ((hostTrack.animatedProperties & AnimatedProperties.ImageIndex) != 0)
                {
                    clip.spriteTracks.Add(BuildSpriteTrack(hostTrack, targetId));
                }
            }

            for (int markerIndex = 0; markerIndex < hostClip.soundMarkers.Count; markerIndex++)
            {
                AnimationClipSO.SoundMarker hostMarker = hostClip.soundMarkers[markerIndex];
                if (hostMarker == null)
                {
                    continue;
                }
                if (!soundTypesUsed.Contains(hostMarker.type))
                {
                    soundTypesUsed.Add(hostMarker.type);
                }

                EventMarker marker = new EventMarker();
                marker.normalizedTime = math.saturate(hostMarker.normalizedTime);
                marker.eventKey = SoundEventKeyFor(hostMarker.type);
                marker.intParam = (int)hostMarker.type;
                marker.floatParam = 0f;
                clip.events.Add(marker);
            }
            clip.events.Sort(CompareMarkerTime);

            CreateOrReplaceAsset(clip, OutputFolder + "/" + hostClip.name + ".asset");
            return clip;
        }

        private static TransformTrack BuildTransformTrack(
            AnimationClipSO.PartTrack hostTrack,
            uint targetId,
            AnimatedChannels channels)
        {
            TransformTrack track = new TransformTrack();
            track.targetId = targetId;
            track.blendOp = ToBlendOp(hostTrack.blendMode);
            track.channels = channels;

            for (int keyIndex = 0; keyIndex < hostTrack.keyframes.Count; keyIndex++)
            {
                AnimationClipSO.Keyframe hostKey = hostTrack.keyframes[keyIndex];
                TransformKey key = new TransformKey();
                key.normalizedTime = math.saturate(hostKey.normalizedTime);
                key.position = new float3(hostKey.position.x, hostKey.position.y, hostKey.position.z);

                // Degrees on both sides: ClipRegistryBuilder converts to radians once at bake
                // (§4.5 point 2), so the authored value passes through untouched.
                key.rotation = new float3(0f, 0f, hostKey.rotation);
                key.scale = new float3(hostKey.scale.x, hostKey.scale.y, 1f);
                key.interpolation = ToInterpolation(
                    hostKey.overrideInterpolation ? hostKey.interpolationOverride : hostTrack.interpolation);
                track.keys.Add(key);
            }
            return track;
        }

        private static SpriteTrack BuildSpriteTrack(AnimationClipSO.PartTrack hostTrack, uint targetId)
        {
            SpriteTrack track = new SpriteTrack();
            track.targetId = targetId;
            track.mode = SpriteFrameMode.Slice;

            // Absolute, matching what the host meant. Relative slices (amendment A37) are for facing
            // views, which no existing host clip expresses — converting to relative would silently
            // reinterpret every authored frame as an offset.
            track.sliceSpace = SpriteSliceSpace.Absolute;

            for (int keyIndex = 0; keyIndex < hostTrack.keyframes.Count; keyIndex++)
            {
                AnimationClipSO.Keyframe hostKey = hostTrack.keyframes[keyIndex];
                SpriteKey key = new SpriteKey();
                key.normalizedTime = math.saturate(hostKey.normalizedTime);

                // -1 keeps its "no change" meaning on both sides.
                key.sliceIndex = hostKey.imageIndex;
                key.atlasRect = ClipSampler.IdentityAtlasRect;
                track.keys.Add(key);
            }
            return track;
        }

        // -------------------------------------------------------------------------------------
        // Enum mapping
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Maps the host's blend mode onto the package's.
        /// </summary>
        /// <remarks>
        /// <strong>Mapped by name, never by value, and that is not pedantry.</strong> The two enums
        /// are numerically inverted — host <c>BlendMode { Additive = 0, Override = 1 }</c> against
        /// package <c>TrackBlendOp { Override = 0, Additive = 1 }</c> — so a cast would flip 95 of
        /// the host's 96 tracks from additive to override and change what every clip means.
        /// </remarks>
        private static TrackBlendOp ToBlendOp(BlendMode hostBlendMode)
        {
            return hostBlendMode == BlendMode.Override ? TrackBlendOp.Override : TrackBlendOp.Additive;
        }

        /// <summary>
        /// Maps the host's interpolation onto the package's. By name, for the same reason as
        /// <see cref="ToBlendOp"/> — these two happen to agree numerically today, and relying on
        /// that would make either enum unsafe to reorder.
        /// </summary>
        private static Interpolation ToInterpolation(InterpolationMode hostInterpolation)
        {
            switch (hostInterpolation)
            {
                case InterpolationMode.Step: return Interpolation.Step;
                case InterpolationMode.EaseIn: return Interpolation.EaseIn;
                case InterpolationMode.EaseOut: return Interpolation.EaseOut;
                case InterpolationMode.EaseInOut: return Interpolation.EaseInOut;
                default: return Interpolation.Linear;
            }
        }

        /// <summary>
        /// Maps the host's per-axis property flags onto the package's channel mask.
        /// </summary>
        /// <remarks>
        /// The package masks x and y together (<c>PositionXY</c>, <c>Scale</c>) where the host masks
        /// them separately, so this is lossy in principle. It is lossless in practice for this
        /// content: a scan of all 96 authored tracks found only the values 127 (All), 64
        /// (ImageIndex), 8 (Rotation) and 11 (PositionX|PositionY|Rotation) — no track ever masks one
        /// axis without its partner. A future host track that did would silently gain the other axis,
        /// which is why this is written down rather than assumed.
        /// </remarks>
        private static AnimatedChannels ToChannels(AnimatedProperties hostProperties)
        {
            AnimatedChannels channels = AnimatedChannels.None;
            if ((hostProperties & (AnimatedProperties.PositionX | AnimatedProperties.PositionY)) != 0)
            {
                channels |= AnimatedChannels.PositionXY;
            }
            if ((hostProperties & AnimatedProperties.PositionZ) != 0)
            {
                channels |= AnimatedChannels.PositionZ;
            }
            if ((hostProperties & AnimatedProperties.Rotation) != 0)
            {
                channels |= AnimatedChannels.Rotation;
            }
            if ((hostProperties & (AnimatedProperties.ScaleX | AnimatedProperties.ScaleY)) != 0)
            {
                channels |= AnimatedChannels.Scale;
            }
            return channels;
        }

        /// <summary>
        /// The reserved-safe event key for a host sound type. User keys start at 16 (validation
        /// rule V09); 0–15 belong to the package's built-ins.
        /// </summary>
        private static uint SoundEventKeyFor(SoundType soundType)
        {
            return (uint)ReservedEventKeys.FirstUserKey + (uint)soundType;
        }

        private static int CompareMarkerTime(EventMarker first, EventMarker second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        // -------------------------------------------------------------------------------------
        // Generated code
        // -------------------------------------------------------------------------------------

        private static void WriteClipConstants(List<ClipAsset> clips)
        {
            StringBuilder source = new StringBuilder();
            source.AppendLine("// Generated by HostClipConverter. Do not edit by hand.");
            source.AppendLine("// Re-running the converter mints fresh ids and invalidates every value below.");
            source.AppendLine();
            source.AppendLine("namespace StitchPunk.AnimationToolkitMigration");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>Stable clip ids for the converted host animation set.</summary>");
            source.AppendLine("    public static class StitchPunkClips");
            source.AppendLine("    {");
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                source.AppendLine(
                    "        public const ulong " + SanitizeIdentifier(clip.name) + " = "
                    + clip.Id.Value + "UL;");
            }
            source.AppendLine("    }");
            source.AppendLine("}");

            System.IO.File.WriteAllText(
                GeneratedCodeFolder + "/StitchPunkClips.cs", source.ToString());
        }

        private static void WriteSoundEventKeys(List<SoundType> soundTypesUsed)
        {
            StringBuilder source = new StringBuilder();
            source.AppendLine("// Generated by HostClipConverter. Do not edit by hand.");
            source.AppendLine();
            source.AppendLine("namespace StitchPunk.AnimationToolkitMigration");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine("    /// Animation event keys for the host's sound markers. Values start at the package's");
            source.AppendLine("    /// first user key (16) so they can never collide with a reserved built-in.");
            source.AppendLine("    /// </summary>");
            source.AppendLine("    public static class SoundEventKeys");
            source.AppendLine("    {");
            for (int soundIndex = 0; soundIndex < soundTypesUsed.Count; soundIndex++)
            {
                SoundType soundType = soundTypesUsed[soundIndex];
                source.AppendLine(
                    "        public const uint " + SanitizeIdentifier(soundType.ToString()) + " = "
                    + SoundEventKeyFor(soundType) + "u;");
            }
            source.AppendLine("    }");
            source.AppendLine("}");

            System.IO.File.WriteAllText(
                GeneratedCodeFolder + "/SoundEventKeys.cs", source.ToString());
        }

        private static string SanitizeIdentifier(string rawName)
        {
            StringBuilder identifier = new StringBuilder();
            for (int characterIndex = 0; characterIndex < rawName.Length; characterIndex++)
            {
                char character = rawName[characterIndex];
                identifier.Append(char.IsLetterOrDigit(character) ? character : '_');
            }
            if (identifier.Length > 0 && char.IsDigit(identifier[0]))
            {
                identifier.Insert(0, '_');
            }
            return identifier.ToString();
        }

        // -------------------------------------------------------------------------------------
        // Plumbing
        // -------------------------------------------------------------------------------------

        private static List<AnimationClipSO> LoadHostClips(out List<string> skipped)
        {
            List<AnimationClipSO> clips = new List<AnimationClipSO>();
            skipped = new List<string>();

            string[] guids = AssetDatabase.FindAssets("t:AnimationClipSO", new[] { SourceFolder });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                AnimationClipSO hostClip = AssetDatabase.LoadAssetAtPath<AnimationClipSO>(path);
                if (hostClip == null)
                {
                    continue;
                }
                if (hostClip.name == SupersededDirectionClipName)
                {
                    skipped.Add(hostClip.name + " (superseded by the 8-direction set, amendment A37)");
                    continue;
                }
                clips.Add(hostClip);
            }
            clips.Sort(CompareClipName);
            return clips;
        }

        private static int CompareClipName(AnimationClipSO first, AnimationClipSO second)
        {
            return string.CompareOrdinal(first.name, second.name);
        }

        private static void ReportValidation(
            RigAsset rig,
            List<ClipAsset> clips,
            ClipSetAsset clipSet,
            List<string> skipped)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();
            messages.AddRange(ClipValidation.ValidateRig(rig));
            messages.AddRange(ClipValidation.ValidateSet(clipSet));

            int errorCount = 0;
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                string line = "[HostClipConverter] " + message.severity + " " + message.code + ": "
                    + message.text;
                if (message.severity == ValidationSeverity.Error)
                {
                    errorCount++;
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[HostClipConverter] Converted " + clips.Count + " clips into " + OutputFolder);
            summary.AppendLine("  Rig: " + rig.targets.Count + " targets, " + rig.layers.Count
                + " layers (Direction dropped per A37)");
            for (int skippedIndex = 0; skippedIndex < skipped.Count; skippedIndex++)
            {
                summary.AppendLine("  Skipped: " + skipped[skippedIndex]);
            }
            summary.AppendLine("  Validation findings: " + messages.Count + " (" + errorCount + " errors)");

            if (errorCount > 0)
            {
                Debug.LogError(summary.ToString());
            }
            else
            {
                Debug.Log(summary.ToString());
            }
        }

        private static void EnsureFolder(string parentFolder, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
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
