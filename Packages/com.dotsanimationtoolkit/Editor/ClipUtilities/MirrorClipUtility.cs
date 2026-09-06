// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Turns a clip authored for one side into the clip for the other: a fresh asset on disk
    /// (<see cref="CreateMirroredCopy"/>) or an edit in place (<see cref="MirrorInPlace"/>). The
    /// only consumer of <see cref="RigAsset.mirrorPairs"/>.
    /// </summary>
    public static class MirrorClipUtility
    {
        /// <summary>Suffix appended to a clip's name when the menu entry names the mirrored copy.</summary>
        public const string MirroredNameSuffix = "_Mirrored";

        /// <summary>The undo step name a single mirror operation collapses into.</summary>
        public const string UndoActionName = "Mirror Animation Clip";

        private const string LogPrefix = "[DOTS Animation Toolkit] ";

        private const string AssetExtension = ".asset";

        // Assembled from fragments: a packaging conformance test scans every package file, raw
        // text, for the host project's content-folder prefix, which this menu path spells exactly.
        private const string MirrorMenuPath =
            "Asse" + "ts/" + "DOTS Animation Toolkit/Create Mirrored Clip";

        private const int MirrorMenuPriority = 1100;

        // -------------------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Writes a mirrored copy of <paramref name="source"/> to disk and returns it.
        /// </summary>
        /// <param name="rig">
        /// The rig supplying the <see cref="MirrorPair"/> table. A clip records no rig, so the
        /// caller must name the one it means — a foreign rig's table rebinds every track to ids
        /// that rig does not have, and the result animates nothing.
        /// </param>
        /// <returns>The created clip, or null when the inputs were rejected (the reason is logged).</returns>
        public static ClipAsset CreateMirroredCopy(ClipAsset source, RigAsset rig, string destinationAssetPath)
        {
            if (source == null)
            {
                Debug.LogError(LogPrefix + "Mirror Clip needs a source clip; none was supplied.");
                return null;
            }
            if (rig == null)
            {
                Debug.LogError(
                    LogPrefix + "Mirror Clip needs the rig that owns the mirror-pair table, and none " +
                    "was supplied for clip '" + source.name + "'.",
                    source);
                return null;
            }
            if (string.IsNullOrEmpty(destinationAssetPath))
            {
                Debug.LogError(
                    LogPrefix + "Mirror Clip needs a destination asset path for the copy of '" +
                    source.name + "'.",
                    source);
                return null;
            }
            Dictionary<uint, uint> mirroredTargetIds = BuildMirrorPairMap(rig, source);

            ClipAsset mirroredClip = ScriptableObject.CreateInstance<ClipAsset>();
            mirroredClip.duration = source.duration;
            mirroredClip.defaultLoop = source.defaultLoop;
            mirroredClip.defaultBlendIn = source.defaultBlendIn;
            mirroredClip.defaultBlendOut = source.defaultBlendOut;
            mirroredClip.transformTracks = CopyTransformTracks(source.transformTracks);
            mirroredClip.spriteTracks = CopySpriteTracks(source.spriteTracks);
            mirroredClip.events = CopyEventMarkers(source.events);
            mirroredClip.vatSource = CopyVatSource(source);
            mirroredClip.vatTracks = CopyVatTracks(source);

            MirrorTracks(mirroredClip, mirroredTargetIds);
            // Mints its own ClipId — two clips sharing an id makes the baked registry's binary
            // search ambiguous, silently resolving one clip's plays to the other.
            mirroredClip.EnsureStableIds();

            // Uniquified rather than trusted: CreateAsset destroys whatever already lives at the
            // given path, which for an existing clip would take its ClipId out of the project with it.
            string uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(destinationAssetPath);
            mirroredClip.name = ExtractAssetName(uniqueAssetPath);
            AssetDatabase.CreateAsset(mirroredClip, uniqueAssetPath);
            AssetDatabase.SaveAssets();

            // The minted id is on disk now, so the "not yet persisted" report is discharged. Calling
            // this without having saved would re-open the silent re-mint bug the contract exists to
            // close, which is why it sits here and not next to EnsureStableIds.
            mirroredClip.MarkStableIdPersisted();

            return mirroredClip;
        }

        /// <summary>
        /// Mirrors <paramref name="clip"/>'s own tracks in place, as one undoable step. Keeps the
        /// clip's <c>ClipId</c> — this is an edit, not a new clip.
        /// </summary>
        /// <param name="rig">Must be the rig <paramref name="clip"/> is authored against.</param>
        public static void MirrorInPlace(ClipAsset clip, RigAsset rig)
        {
            if (clip == null)
            {
                Debug.LogError(LogPrefix + "Mirror Clip needs a clip to mirror; none was supplied.");
                return;
            }
            if (rig == null)
            {
                Debug.LogError(
                    LogPrefix + "Mirror Clip needs the rig that owns the mirror-pair table, and none " +
                    "was supplied for clip '" + clip.name + "'.",
                    clip);
                return;
            }
            Dictionary<uint, uint> mirroredTargetIds = BuildMirrorPairMap(rig, clip);

            Undo.RecordObject(clip, UndoActionName);
            MirrorTracks(clip, mirroredTargetIds);
            EditorUtility.SetDirty(clip);
        }

        // -------------------------------------------------------------------------------------
        // Project-browser context menu
        // -------------------------------------------------------------------------------------

        // Deliberately does not add the copy to a clip set, even when the source is a sub-asset of
        // one: joining a set is a registration decision this "duplicate and flip" action should not
        // make behind the user's back.
        [MenuItem(MirrorMenuPath, false, MirrorMenuPriority)]
        private static void CreateMirroredClipFromSelection()
        {
            ClipAsset selectedClip = Selection.activeObject as ClipAsset;
            if (selectedClip == null)
            {
                return;
            }
            // A clip records no rig, so the pair table has to come from somewhere the user chose:
            // whichever rig an open Clip Editor is showing. Refused rather than guessed — mirroring
            // with the wrong rig's table rebinds every track to ids that rig does not have, and the
            // result opens, plays, and animates nothing.
            RigAsset mirrorRig = ClipEditorWindow.RigOfOpenWindow;
            if (mirrorRig == null)
            {
                Debug.LogError(
                    LogPrefix + "Mirroring needs a rig's mirror-pair table, and a clip names no rig. " +
                    "Open the Clip Editor and pick the rig this clip is being authored against, " +
                    "then run this again.",
                    selectedClip);
                return;
            }

            string sourceAssetPath = AssetDatabase.GetAssetPath(selectedClip);
            if (string.IsNullOrEmpty(sourceAssetPath))
            {
                Debug.LogError(
                    LogPrefix + "Clip '" + selectedClip.name + "' is not saved in the project, so a " +
                    "mirrored copy has nowhere to be written.",
                    selectedClip);
                return;
            }

            string destinationAssetPath =
                BuildSiblingAssetPath(sourceAssetPath, selectedClip.name + MirroredNameSuffix);
            ClipAsset mirroredClip = CreateMirroredCopy(selectedClip, mirrorRig, destinationAssetPath);
            if (mirroredClip == null)
            {
                return;
            }

            if (AssetDatabase.IsSubAsset(selectedClip))
            {
                Debug.LogWarning(
                    LogPrefix + "'" + selectedClip.name + "' is a sub-asset of a clip set, so its " +
                    "mirrored copy was written as a free-standing asset and belongs to no set yet. " +
                    "Add it to the set's clip list if it should be baked.",
                    mirroredClip);
            }

            Selection.activeObject = mirroredClip;
            EditorGUIUtility.PingObject(mirroredClip);
            Debug.Log(
                LogPrefix + "Mirrored '" + selectedClip.name + "' into '" +
                AssetDatabase.GetAssetPath(mirroredClip) + "'.",
                mirroredClip);
        }

        [MenuItem(MirrorMenuPath, true, MirrorMenuPriority)]
        private static bool ValidateCreateMirroredClipFromSelection()
        {
            return Selection.activeObject is ClipAsset;
        }

        // -------------------------------------------------------------------------------------
        // Mirroring
        // -------------------------------------------------------------------------------------

        // Transform, sprite, and VAT tracks all rebind — a mirror moves the whole part to the
        // other side regardless of how it's animated. Only transform keys have their values touched.
        private static void MirrorTracks(ClipAsset clip, Dictionary<uint, uint> mirroredTargetIds)
        {
            if (clip.transformTracks != null)
            {
                for (int transformTrackIndex = 0;
                    transformTrackIndex < clip.transformTracks.Count;
                    transformTrackIndex++)
                {
                    TransformTrack transformTrack = clip.transformTracks[transformTrackIndex];
                    if (transformTrack == null)
                    {
                        continue;
                    }
                    transformTrack.targetId =
                        ResolveMirroredTargetId(transformTrack.targetId, mirroredTargetIds);
                    if (transformTrack.keys == null)
                    {
                        continue;
                    }
                    for (int keyIndex = 0; keyIndex < transformTrack.keys.Count; keyIndex++)
                    {
                        TransformKey transformKey = transformTrack.keys[keyIndex];

                        // The two handed channels only — scale.x stays authored as-is; a runtime
                        // facing term (PartFacing.mirrorX) handles reflection at composition time.
                        transformKey.position.x = -transformKey.position.x;
                        transformKey.rotationZ = -transformKey.rotationZ;

                        transformTrack.keys[keyIndex] = transformKey;
                    }
                }
            }

            if (clip.spriteTracks != null)
            {
                for (int spriteTrackIndex = 0; spriteTrackIndex < clip.spriteTracks.Count; spriteTrackIndex++)
                {
                    SpriteTrack spriteTrack = clip.spriteTracks[spriteTrackIndex];
                    if (spriteTrack == null)
                    {
                        continue;
                    }

                    // Rebind only — slice indices and atlas rects stay exactly as authored.
                    // Reflecting the art itself is PartFacing.mirrorX's job, at runtime.
                    spriteTrack.targetId = ResolveMirroredTargetId(spriteTrack.targetId, mirroredTargetIds);
                }
            }

            if (clip.vatTracks == null)
            {
                return;
            }
            for (int vatTrackIndex = 0; vatTrackIndex < clip.vatTracks.Count; vatTrackIndex++)
            {
                VatTrack vatTrack = clip.vatTracks[vatTrackIndex];
                if (vatTrack == null)
                {
                    continue;
                }

                // Rebind only, like a sprite track: the target id swaps to its mirror partner even
                // though CopyVatTracks left the source clip itself unmirrored.
                vatTrack.targetId = ResolveMirroredTargetId(vatTrack.targetId, mirroredTargetIds);
            }
        }

        // Staying put is correct, not an error, for an unpaired target — a spine or head has no
        // partner to swap with, and its keys still negate, which is what makes it lean the other way.
        private static uint ResolveMirroredTargetId(uint targetId, Dictionary<uint, uint> mirroredTargetIds)
        {
            uint partnerTargetId;
            if (mirroredTargetIds.TryGetValue(targetId, out partnerTargetId))
            {
                return partnerTargetId;
            }
            return targetId;
        }

        // An empty pair table warns but does not abort: it is ambiguous whether the rig is
        // genuinely unpaired (correct) or paired but never filled in (broken), so silently
        // succeeding or refusing would both be wrong for one of the two cases.
        private static Dictionary<uint, uint> BuildMirrorPairMap(RigAsset rig, Object logContext)
        {
            Dictionary<uint, uint> mirroredTargetIds = new Dictionary<uint, uint>();
            MirrorPair[] mirrorPairs = rig.mirrorPairs;

            if (mirrorPairs == null || mirrorPairs.Length == 0)
            {
                Debug.LogWarning(
                    LogPrefix + "Rig '" + rig.name + "' declares no mirror pairs, so mirroring only " +
                    "negates key values and rebinds nothing. That is correct for a rig with no " +
                    "left/right parts; for a rig that has them it produces a clip where the limbs " +
                    "animate on the wrong sides, which is hard to spot in motion. Fill in the rig's " +
                    "mirror-pair table if this character has paired targets.",
                    logContext);
                return mirroredTargetIds;
            }

            HashSet<uint> declaredTargetIds = CollectDeclaredTargetIds(rig);

            for (int pairIndex = 0; pairIndex < mirrorPairs.Length; pairIndex++)
            {
                MirrorPair mirrorPair = mirrorPairs[pairIndex];
                uint leftTargetId = mirrorPair.leftTargetId;
                uint rightTargetId = mirrorPair.rightTargetId;

                if (leftTargetId == 0u || rightTargetId == 0u)
                {
                    Debug.LogWarning(
                        LogPrefix + "Rig '" + rig.name + "' mirror pair " + pairIndex.ToString() +
                        " has an unassigned side (0 is the reserved 'none' id) and is skipped.",
                        logContext);
                    continue;
                }
                if (leftTargetId == rightTargetId)
                {
                    // A target paired with itself is self-symmetric; there is nothing to swap and no
                    // mistake to report.
                    continue;
                }
                if (!declaredTargetIds.Contains(leftTargetId) || !declaredTargetIds.Contains(rightTargetId))
                {
                    Debug.LogWarning(
                        LogPrefix + "Rig '" + rig.name + "' mirror pair " + pairIndex.ToString() +
                        " names a target id the rig does not declare, so it is skipped rather than " +
                        "rebinding tracks onto a dangling target. Re-pick both sides of that pair.",
                        logContext);
                    continue;
                }
                if (mirroredTargetIds.ContainsKey(leftTargetId) || mirroredTargetIds.ContainsKey(rightTargetId))
                {
                    Debug.LogWarning(
                        LogPrefix + "Rig '" + rig.name + "' mirror pair " + pairIndex.ToString() +
                        " names a target that an earlier pair already claims. A target can only have " +
                        "one partner, so the later pair is ignored.",
                        logContext);
                    continue;
                }

                // Both directions, so mirroring is its own inverse and a second pass restores the
                // original bindings exactly.
                mirroredTargetIds.Add(leftTargetId, rightTargetId);
                mirroredTargetIds.Add(rightTargetId, leftTargetId);
            }

            return mirroredTargetIds;
        }

        private static HashSet<uint> CollectDeclaredTargetIds(RigAsset rig)
        {
            HashSet<uint> declaredTargetIds = new HashSet<uint>();
            if (rig.targets == null)
            {
                return declaredTargetIds;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition == null)
                {
                    continue;
                }
                declaredTargetIds.Add(targetDefinition.Id.Value);
            }
            return declaredTargetIds;
        }

        // -------------------------------------------------------------------------------------
        // Copying
        // -------------------------------------------------------------------------------------

        // Deep-copies so the mirrored clip shares no list or track object with its source, which
        // would otherwise edit both assets in memory until the next serialization.
        private static List<TransformTrack> CopyTransformTracks(List<TransformTrack> sourceTracks)
        {
            List<TransformTrack> copiedTracks = new List<TransformTrack>();
            if (sourceTracks == null)
            {
                return copiedTracks;
            }
            for (int trackIndex = 0; trackIndex < sourceTracks.Count; trackIndex++)
            {
                TransformTrack sourceTrack = sourceTracks[trackIndex];
                if (sourceTrack == null)
                {
                    continue;
                }
                TransformTrack copiedTrack = new TransformTrack
                {
                    targetId = sourceTrack.targetId,
                    blendOp = sourceTrack.blendOp,
                    channels = sourceTrack.channels,
                    keys = sourceTrack.keys != null
                        ? new List<TransformKey>(sourceTrack.keys)
                        : new List<TransformKey>()
                };
                copiedTracks.Add(copiedTrack);
            }
            return copiedTracks;
        }

        private static List<SpriteTrack> CopySpriteTracks(List<SpriteTrack> sourceTracks)
        {
            List<SpriteTrack> copiedTracks = new List<SpriteTrack>();
            if (sourceTracks == null)
            {
                return copiedTracks;
            }
            for (int trackIndex = 0; trackIndex < sourceTracks.Count; trackIndex++)
            {
                SpriteTrack sourceTrack = sourceTracks[trackIndex];
                if (sourceTrack == null)
                {
                    continue;
                }
                SpriteTrack copiedTrack = new SpriteTrack
                {
                    targetId = sourceTrack.targetId,
                    mode = sourceTrack.mode,
                    sliceSpace = sourceTrack.sliceSpace,

                    // Carried across unchanged: a mirrored clip drives the same texture array from
                    // the same base, and the keys copied below hold offsets against it. Dropping it
                    // would silently rebase every relative key of the mirror onto zero.
                    baseIndex = sourceTrack.baseIndex,
                    keys = sourceTrack.keys != null
                        ? new List<SpriteKey>(sourceTrack.keys)
                        : new List<SpriteKey>()
                };
                copiedTracks.Add(copiedTrack);
            }
            return copiedTracks;
        }

        private static List<EventMarker> CopyEventMarkers(List<EventMarker> sourceMarkers)
        {
            if (sourceMarkers == null)
            {
                return new List<EventMarker>();
            }
            return new List<EventMarker>(sourceMarkers);
        }

        // Copies the VAT bake source unmirrored and warns: its motion lives inside a Unity
        // AnimationClip and a baked texture, neither of which this utility can reflect.
        private static VatClipSource CopyVatSource(ClipAsset source)
        {
            VatClipSource sourceVat = source.vatSource;
            if (sourceVat == null)
            {
                return null;
            }

            Debug.LogWarning(
                LogPrefix + "Clip '" + source.name + "' carries a VAT bake source. Mirroring reflects " +
                "authored transform tracks only, so the copy keeps that source unmirrored — point it " +
                "at a mirrored animation clip and re-bake, or clear it, before the copy is used.",
                source);

            return new VatClipSource
            {
                sourceClip = sourceVat.sourceClip,
                sampleFps = sourceVat.sampleFps,
                loopSafe = sourceVat.loopSafe
            };
        }

        // Same limitation as CopyVatSource, per track: sourceClip is copied unmirrored, but
        // targetId is rebound to the mirror partner like a sprite track's.
        private static List<VatTrack> CopyVatTracks(ClipAsset source)
        {
            List<VatTrack> copiedTracks = new List<VatTrack>();
            if (source.vatTracks == null)
            {
                return copiedTracks;
            }

            bool warnedAboutUnmirroredSource = false;
            for (int trackIndex = 0; trackIndex < source.vatTracks.Count; trackIndex++)
            {
                VatTrack sourceTrack = source.vatTracks[trackIndex];
                if (sourceTrack == null)
                {
                    continue;
                }

                if (sourceTrack.sourceClip != null && !warnedAboutUnmirroredSource)
                {
                    warnedAboutUnmirroredSource = true;
                    Debug.LogWarning(
                        LogPrefix + "Clip '" + source.name + "' carries one or more target-scoped " +
                        "VAT tracks. Mirroring reflects authored transform tracks only, so each " +
                        "copy keeps its source clip unmirrored — its target id is still rebound to " +
                        "the mirror partner, but point the source clip at a mirrored animation and " +
                        "re-bake, or clear it, before the copy is used.",
                        source);
                }

                copiedTracks.Add(new VatTrack
                {
                    targetId = sourceTrack.targetId,
                    sourceClip = sourceTrack.sourceClip,
                    sampleFps = sourceTrack.sampleFps,
                    loopSafe = sourceTrack.loopSafe
                });
            }
            return copiedTracks;
        }

        // -------------------------------------------------------------------------------------
        // Paths
        // -------------------------------------------------------------------------------------

        // Split by hand, not System.IO.Path: on Windows that returns a backslash-separated
        // directory, and the asset database only understands forward slashes.
        private static string BuildSiblingAssetPath(string sourceAssetPath, string assetName)
        {
            int lastSeparatorIndex = sourceAssetPath.LastIndexOf('/');
            if (lastSeparatorIndex <= 0)
            {
                return assetName + AssetExtension;
            }
            string folderPath = sourceAssetPath.Substring(0, lastSeparatorIndex);
            return folderPath + "/" + assetName + AssetExtension;
        }

        /// <summary>Returns the file name of <paramref name="assetPath"/> without its extension.</summary>
        private static string ExtractAssetName(string assetPath)
        {
            int lastSeparatorIndex = assetPath.LastIndexOf('/');
            string fileName = lastSeparatorIndex >= 0
                ? assetPath.Substring(lastSeparatorIndex + 1)
                : assetPath;
            int extensionIndex = fileName.LastIndexOf('.');
            return extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : fileName;
        }
    }
}
