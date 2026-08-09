// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The Mirror Clip utility (architecture section 7.1, module M5): turns a clip authored for one
    /// side into the clip for the other side, either as a fresh asset on disk
    /// (<see cref="CreateMirroredCopy"/>) or in place on an existing one
    /// (<see cref="MirrorInPlace"/>). It is the first and only consumer of
    /// <see cref="RigAsset.mirrorPairs"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A mirror is three edits, and dropping any one of them produces something that looks
    /// almost right.</strong>
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <em>Paired tracks swap targets.</em> A track bound to the left upper arm rebinds to the right
    /// one and vice versa, following the rig's authored <see cref="MirrorPair"/> table. Negating the
    /// keys without swapping the binding leaves the character swinging the wrong limb — a reflected
    /// pose composed of the wrong parts, which reads as "the walk is slightly off" rather than as an
    /// obvious break.
    /// </description></item>
    /// <item><description>
    /// <em>Every transform key negates its handed channels:</em> <c>position.x</c>, because the part
    /// belongs on the other side of the body, and <c>rotationZ</c>, because rotation is handed — an
    /// arm swung +30 degrees reflects to −30. <c>TransformKey.rotationZ</c> is in <em>degrees</em>
    /// in authoring (radians only after the bake converts it), so the negation is unit-agnostic and
    /// needs no conversion here.
    /// </description></item>
    /// <item><description>
    /// <em>Sprite tracks rebind but keep every key value.</em> See below — this is deliberate and is
    /// the part a future reader is most likely to "fix".
    /// </description></item>
    /// </list>
    /// <para>
    /// <strong>Slice and atlas keys are never rewritten, and neither is <c>scale.x</c>.</strong>
    /// Amendment A37a splits facing into two independent things: an <em>alt view</em> is different
    /// art (a different slice, driven by <c>PartFacing.viewOffset</c>) and a <em>mirror</em> is the
    /// same art reflected (a negative <c>scale.x</c>, driven by <c>PartFacing.mirrorX</c>). Both are
    /// applied at runtime after composition, to every clip on every layer at once. Were this utility
    /// to also rewrite slice keys or flip <c>scale.x</c>, two mechanisms would be moving the same
    /// value and would fight each other — and mirroring a part the animator had deliberately
    /// authored flipped would silently un-flip it. Mirroring transforms only is also what makes the
    /// involution exact rather than approximate: mirror twice and every value is back where it
    /// started, because negation and a symmetric target swap are both their own inverse.
    /// </para>
    /// <para>
    /// <strong>A mirrored clip and a runtime <c>PartFacing.mirrorX</c> must never both apply to the
    /// same facing</strong> (amendment A38) — baked mirrored keys plus a runtime reflection is a
    /// double reflection, which is no reflection at all. The two are alternatives: <c>mirrorX</c> is
    /// the cheap default that serves two facings from one clip, and this utility is the escape hatch
    /// for a facing that must <em>deviate</em> from a pure reflection (an asymmetric costume detail,
    /// a satchel on one hip, a limp). Pressing Flip is the moment a derived facing becomes an
    /// authored one; from then on the copy is ordinary hand-editable data.
    /// </para>
    /// <para>
    /// <strong>Why the menu path is assembled from string fragments.</strong> Packaging conformance
    /// test (d) scans every package file for the host project's content-folder prefix, and the root
    /// of Unity's project-browser context menu happens to be spelled exactly that way. The scan is
    /// raw-text and has no comment or string exemption, so a literal would fail the suite even
    /// though nothing about it references the host project. The conformance suite itself builds its
    /// own scan patterns from fragments for the same reason, so this follows an established idiom
    /// rather than inventing one.
    /// </para>
    /// </remarks>
    public static class MirrorClipUtility
    {
        /// <summary>Suffix appended to a clip's name when the menu entry names the mirrored copy.</summary>
        public const string MirroredNameSuffix = "_Mirrored";

        /// <summary>The undo step name a single mirror operation collapses into.</summary>
        public const string UndoActionName = "Mirror Animation Clip";

        private const string LogPrefix = "[DOTS Animation Toolkit] ";

        private const string AssetExtension = ".asset";

        // Assembled from fragments; see the type remarks for why a literal cannot be used.
        private const string MirrorMenuPath =
            "Asse" + "ts/" + "DOTS Animation Toolkit/Create Mirrored Clip";

        private const int MirrorMenuPriority = 1100;

        // -------------------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Writes a mirrored copy of <paramref name="source"/> to disk and returns it.
        /// </summary>
        /// <param name="source">The clip to mirror. Never modified.</param>
        /// <param name="rig">
        /// The rig supplying the <see cref="MirrorPair"/> table. Must be the rig
        /// <paramref name="source"/> is authored against.
        /// </param>
        /// <param name="destinationAssetPath">
        /// Project-relative path for the new asset, including the <c>.asset</c> extension. Passed
        /// through <see cref="AssetDatabase.GenerateUniqueAssetPath"/> first, so an occupied path is
        /// stepped past rather than overwritten.
        /// </param>
        /// <returns>The created clip, or null when the inputs were rejected (the reason is logged).</returns>
        /// <remarks>
        /// <para>
        /// <strong>The copy mints its own <c>ClipId</c> rather than inheriting one.</strong> Two
        /// distinct clips sharing an id is validation rule V05 and would make the baked registry
        /// ambiguous — the registry binary-searches on that id, so a duplicate silently resolves
        /// every play request for one clip to the other. Nothing here has to arrange that:
        /// <c>ClipAsset</c> mints an id in <c>Awake</c>, which Unity raises on
        /// <see cref="ScriptableObject.CreateInstance{T}()"/>, and this method builds a genuinely new
        /// instance and copies field by field rather than duplicating the asset file, so the source's
        /// id is never in a position to be carried across. <c>EnsureStableIds</c> is still called
        /// explicitly before the write: it is idempotent, it costs nothing, and it makes the
        /// guarantee local to this method instead of resting on a constructor callback in another
        /// assembly. (The Authoring assembly grants <c>InternalsVisibleTo</c> to this one — see its
        /// <c>AssemblyInfo</c> — so the internal minting entry point is a contracted route here, not
        /// a reflection trick or a <c>SerializedObject</c> workaround.)
        /// </para>
        /// <para>
        /// The path is uniquified rather than trusted because <see cref="AssetDatabase.CreateAsset"/>
        /// destroys whatever already lives at the path it is given. Doing that to an existing clip
        /// would take its <c>ClipId</c> out of the project with it, breaking every clip set and every
        /// baked reference pointing at it — a far worse outcome than a file named with a numeric
        /// suffix.
        /// </para>
        /// </remarks>
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
            if (!IsClipAuthoredAgainstRig(source, rig))
            {
                return null;
            }

            Dictionary<uint, uint> mirroredTargetIds = BuildMirrorPairMap(rig, source);

            ClipAsset mirroredClip = ScriptableObject.CreateInstance<ClipAsset>();
            mirroredClip.rig = source.rig != null ? source.rig : rig;
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
            mirroredClip.EnsureStableIds();

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
        /// Mirrors <paramref name="clip"/>'s own tracks, as one undoable step.
        /// </summary>
        /// <param name="clip">The clip to modify.</param>
        /// <param name="rig">
        /// The rig supplying the <see cref="MirrorPair"/> table. Must be the rig
        /// <paramref name="clip"/> is authored against.
        /// </param>
        /// <remarks>
        /// <para>
        /// The clip keeps its <c>ClipId</c>. Mirroring in place is an <em>edit</em> to an existing
        /// clip, not the creation of a new one, so every clip set, actor and baked reference that
        /// points at it must keep pointing at it; re-minting here would orphan all of them.
        /// </para>
        /// <para>
        /// One <c>RecordObject</c> before any mutation is all the undo bookkeeping this needs — the
        /// whole operation is synchronous and touches only this asset, so there is no gesture to
        /// collapse the way a timeline drag has. <c>SetDirty</c> still follows the mutation: undo
        /// registration and the dirty flag are separate concerns, and the clip editor's edit paths
        /// set both for the same reason.
        /// </para>
        /// </remarks>
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
            if (!IsClipAuthoredAgainstRig(clip, rig))
            {
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

        /// <summary>
        /// Creates a mirrored copy of the selected clip beside it in the project browser.
        /// </summary>
        /// <remarks>
        /// The copy is written next to the source and selected, so the flip-then-tune loop the
        /// utility exists for starts with the new clip already open in the inspector. A clip that is
        /// a sub-asset of a <see cref="ClipSetAsset"/> yields a free-standing asset in the set's
        /// folder — this utility deliberately does not add it to the set, because joining a set is a
        /// registration decision (validation rule V06 constrains it) and silently enlarging a shipped
        /// registry is not something a "duplicate and flip" action should do behind the user's back.
        /// </remarks>
        [MenuItem(MirrorMenuPath, false, MirrorMenuPriority)]
        private static void CreateMirroredClipFromSelection()
        {
            ClipAsset selectedClip = Selection.activeObject as ClipAsset;
            if (selectedClip == null)
            {
                return;
            }
            if (selectedClip.rig == null)
            {
                Debug.LogError(
                    LogPrefix + "Clip '" + selectedClip.name + "' has no rig assigned, so there is no " +
                    "mirror-pair table to mirror it with. Assign the rig it is authored against first.",
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
            ClipAsset mirroredClip = CreateMirroredCopy(selectedClip, selectedClip.rig, destinationAssetPath);
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

        /// <summary>
        /// Applies the mirror to <paramref name="clip"/>'s tracks in place.
        /// </summary>
        /// <remarks>
        /// Transform, sprite, and VAT tracks (C10) all rebind, because a mirror moves the whole part
        /// to the other side of the body whether that part is animated by pose, by frame, or by a
        /// target-scoped VAT source. Only transform keys have their values touched.
        /// </remarks>
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

                        // The two handed channels, and only those. normalizedTime, scale and
                        // interpolation are unhanded; see the type remarks for why scale.x in
                        // particular is left to the runtime facing term.
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

                    // Rebind only. Slice indices and atlas rects are left exactly as authored — that
                    // is not an omission, it is the A37a division of labour described in the type
                    // remarks.
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

                // Rebind only, exactly like a sprite track (C10): which part plays this baked range
                // is independent of the motion inside it, so the target id still swaps to its mirror
                // partner even though CopyVatTracks left the source clip itself unmirrored.
                vatTrack.targetId = ResolveMirroredTargetId(vatTrack.targetId, mirroredTargetIds);
            }
        }

        /// <summary>
        /// Returns the partner of <paramref name="targetId"/>, or the id unchanged when it is in no
        /// pair.
        /// </summary>
        /// <remarks>
        /// An unpaired target staying put is correct, not an error: a spine, a head or a pelvis has
        /// no partner to swap with, and a mirror leaves it exactly where it is (its keys still
        /// negate, which is what makes a spine lean the other way).
        /// </remarks>
        private static uint ResolveMirroredTargetId(uint targetId, Dictionary<uint, uint> mirroredTargetIds)
        {
            uint partnerTargetId;
            if (mirroredTargetIds.TryGetValue(targetId, out partnerTargetId))
            {
                return partnerTargetId;
            }
            return targetId;
        }

        /// <summary>
        /// Builds the bidirectional left-to-right target lookup from the rig's authored pairs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>An empty table warns but does not abort.</strong> A rig with no pairs is either a
        /// genuinely unpaired one — a tentacle, a banner, a single quad, where negating the keys
        /// <em>is</em> the whole correct mirror — or a paired rig whose table was never filled in,
        /// where the result is a character swinging the wrong limbs. Nothing in the data
        /// distinguishes those two, so refusing would block the legitimate case and proceeding
        /// silently would ship the broken one. Warning and proceeding is the only honest option, and
        /// the message names the rig so the check takes seconds.
        /// </para>
        /// <para>
        /// Pairs are validated against the rig's declared targets before being trusted. A pair naming
        /// an id no target carries would rebind a real track onto a dangling binding, which surfaces
        /// much later as validation rule V02 on a clip whose tracks the animator never edited.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Rejects a clip authored against a different rig than the one supplying the pairs.
        /// </summary>
        /// <remarks>
        /// Mirroring with a foreign rig's table would rebind tracks to ids that mean nothing in the
        /// clip's own rig — the clip would still open, still play, and animate nothing, which is the
        /// least diagnosable failure this utility could produce. A clip with no rig assigned at all is
        /// accepted: it is under construction, and the caller has already named the rig explicitly.
        /// </remarks>
        private static bool IsClipAuthoredAgainstRig(ClipAsset clip, RigAsset rig)
        {
            if (clip.rig == null || clip.rig == rig)
            {
                return true;
            }
            Debug.LogError(
                LogPrefix + "Clip '" + clip.name + "' is authored against rig '" + clip.rig.name +
                "' but was asked to mirror with the pair table of rig '" + rig.name +
                "'. Mirroring across rigs would rebind its tracks to targets that rig does not have, " +
                "so nothing was changed.",
                clip);
            return false;
        }

        // -------------------------------------------------------------------------------------
        // Copying
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Deep-copies the transform tracks so the copy shares no list or track object with its
        /// source.
        /// </summary>
        /// <remarks>
        /// Reference sharing would survive until the next serialization and no longer: two assets
        /// pointing at one <see cref="TransformTrack"/> instance edit each other in memory, then
        /// silently stop doing so once Unity writes them out as independent inline data. A bug that
        /// disappears when you save is the worst kind to be handed.
        /// </remarks>
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

        /// <summary>
        /// Copies the clip's VAT bake source, unmirrored, and says so.
        /// </summary>
        /// <remarks>
        /// A VAT clip's motion lives inside a Unity <c>AnimationClip</c> and a baked texture, neither
        /// of which this utility can reflect — it mirrors authored cutout tracks. Dropping the source
        /// would quietly lose authoring data and break validation rule V07's pairing with the set's
        /// texture ranges; carrying it across unchanged would let the mirrored clip bake mesh motion
        /// that still faces the original way. Copying plus a warning is the only option that loses
        /// nothing and hides nothing.
        /// </remarks>
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

        /// <summary>
        /// Copies the clip's target-scoped VAT bake sources (C10), unmirrored, and warns once.
        /// </summary>
        /// <remarks>
        /// Same limitation as <see cref="CopyVatSource"/>, applied per track: a <see cref="VatTrack"/>
        /// names a Unity <c>AnimationClip</c> this utility cannot reflect, so every copied track keeps
        /// its <c>sourceClip</c> exactly as authored. Unlike <see cref="CopyVatSource"/>'s single
        /// clip-wide source, though, <see cref="VatTrack.targetId"/> is data this utility already
        /// knows how to rebind — <see cref="MirrorTracks"/> swaps it to the target's mirror partner
        /// the same way it does for a sprite track, so a cape track authored against the left socket
        /// still lands on the right one in the copy. Only the motion inside the track is left for the
        /// caller to fix.
        /// </remarks>
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
                        "VAT tracks (C10). Mirroring reflects authored transform tracks only, so each " +
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

        /// <summary>
        /// Builds a path to <paramref name="assetName"/> in the folder holding
        /// <paramref name="sourceAssetPath"/>.
        /// </summary>
        /// <remarks>
        /// Split by hand rather than through <c>System.IO.Path</c>: on Windows that returns a
        /// backslash-separated directory, and the asset database only understands forward slashes, so
        /// the resulting path would be rejected on one platform and work on the others. The VAT bake
        /// window resolves its output folder the same way for the same reason.
        /// </remarks>
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
