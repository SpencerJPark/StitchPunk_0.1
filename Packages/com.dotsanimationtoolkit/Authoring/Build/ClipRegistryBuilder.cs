// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Turns one bind — a <see cref="RigAsset"/> and the <see cref="ClipSetAsset"/>s played on it —
    /// into the single <see cref="ClipRegistryBlob"/> the runtime reads, together with the content
    /// hash that keys it in the <c>BlobAssetStore</c>. The same builder serves entity baking and the
    /// editor's preview, so preview and runtime are structurally guaranteed to sample identical data.
    /// </summary>
    // The build is a pure function of the source assets: canonical ordering discards authoring list
    // order, value conversions (degrees to radians, clamped blends) run once at bake, and the
    // content hash is taken over float bit patterns rather than formatted values — so the same
    // assets produce the same blob and hash on every machine and in every session. Key order is not
    // re-derived: an unsorted track is an error, and Build runs that gate first.
    public static class ClipRegistryBuilder
    {
        /// <summary>Layout version stamped into <see cref="ClipRegistryBlob.schemaVersion"/> and mixed into the dedup hash. Bump on any blob layout or canonical-stream change.</summary>
        // 2: clipIndexById removed (dense index is now the binary-search position); localBounds
        //    renamed to offsetBounds; hash stream widened to cover target ids/extents, VAT info, debug name.
        // 3: VatClipRange gained targetId; ClipBlob gained the (then-unfilled) vatTargetRanges array.
        // 4: vatTargetRanges actually filled from multi-source VAT tracks; hash stream widened to cover it.
        // 5: per-key Bezier handles on transform keys; per-track baseIndex and per-key SpriteIndexMode
        //    on sprite tracks; sliceSpace joins the content hash.
        // 6: transform data becomes 3D — rotation is three Euler angles, scale is a float3.
        // 7: EventMarkerBlob gains windowSeconds (appended, not reshaped).
        // 8: ClipBlob gains a billboardTracks array (appended, not reshaped).
        // 9: setKey now holds a bind key (rig folded with every bound set), not one set's id alone;
        //    the hash stream's clip order is the union across sets. Same struct layout, different meaning.
        // 10: layerCount removed — a registry is per (rig, sets); the layer count now belongs to
        //     ActorProfileBlob, since one registry can serve profiles with different layer counts.
        public const int SchemaVersion = 10;

        // A store hit and a store miss both end with every actor referencing the same blob — the
        // only difference is the work done — so if the probe ever stopped matching Build's key,
        // every crowd would silently rebuild its registry per actor with nothing to notice. This
        // counter is the seam that makes that difference assertable in a test.
        // UNITY_EDITOR-guarded, not just internal: an unguarded counter would exist (and get
        // mutated) in a shipped player for a seam only baking and editor preview ever read.
        /// <summary>Number of times <see cref="Build"/> has allocated a persistent blob this session.</summary>
#if UNITY_EDITOR
        internal static int BuildInvocationCount
        {
            get { return System.Threading.Volatile.Read(ref buildInvocationCount); }
        }

        private static int buildInvocationCount;

        /// <summary>Resets <see cref="BuildInvocationCount"/> so a test can measure one bake.</summary>
        internal static void ResetBuildInvocationCount()
        {
            System.Threading.Volatile.Write(ref buildInvocationCount, 0);
        }
#endif

        /// <summary>Builds the registry blob for one bind, together with its <c>BlobAssetStore</c> dedup key.</summary>
        /// <param name="registry">
        /// The built blob, allocated with <see cref="Allocator.Persistent"/>. Ownership passes to
        /// the caller — in entity baking, straight to <c>Baker.AddBlobAssetWithCustomHash</c>. A
        /// caller that does not hand it to a store owns the disposal itself. To avoid building a
        /// blob only to discard it on a store hit, ask for the key first with
        /// <see cref="TryComputeContentHash"/>.
        /// </param>
        /// <param name="contentHash">
        /// The dedup key: the 64-bit content hash in the low two words, the schema version, and the
        /// folded bind key. Two different binds never dedup onto one blob even with identical
        /// content, since the bind key is part of both the key and the hashed stream.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rig"/> is null.</exception>
        /// <exception cref="ClipValidationException">Thrown when the bind carries any validation error; the exception lists every offending rule code.</exception>
        public static void Build(
            RigAsset rig,
            IReadOnlyList<ClipSetAsset> clipSets,
            out BlobAssetReference<ClipRegistryBlob> registry,
            out Unity.Entities.Hash128 contentHash)
        {
            if (rig == null)
            {
                throw new ArgumentNullException(nameof(rig));
            }

            List<ClipSetAsset> canonicalClipSets = BuildCanonicalClipSets(clipSets);
            ValidateForBakeOrThrow(rig, canonicalClipSets);

#if UNITY_EDITOR
            System.Threading.Interlocked.Increment(ref buildInvocationCount);
#endif
            registry = BuildValidatedBlob(rig, canonicalClipSets, Allocator.Persistent);
            contentHash = ComposeDedupKey(
                HashRegistry(registry), ComposeBindKey(rig, canonicalClipSets));
        }

        // The hash is defined over the finished blob, so this builds one internally into
        // Allocator.Temp and releases it before returning — nothing is left for the caller to own,
        // which is why it can be called on a store hit without leaking. Not free: it runs the same
        // canonicalisation Build does, just without the persistent allocation or store insert.
        /// <summary>Computes the <c>BlobAssetStore</c> dedup key of a bind without handing the caller a blob to own, so a baker can probe the store before deciding to build.</summary>
        /// <param name="contentHash">The dedup key on success, byte-identical to <see cref="Build"/>'s; <c>default</c> on failure.</param>
        /// <returns>False when <paramref name="rig"/> is null or the bind carries validation errors — it is not bakeable, so it has no key.</returns>
        public static bool TryComputeContentHash(
            RigAsset rig,
            IReadOnlyList<ClipSetAsset> clipSets,
            out Unity.Entities.Hash128 contentHash)
        {
            contentHash = default;
            if (rig == null)
            {
                return false;
            }

            List<ClipSetAsset> canonicalClipSets = BuildCanonicalClipSets(clipSets);
            List<ValidationMessage> validationMessages =
                ClipValidation.ValidateBind(rig, canonicalClipSets, ValidationStage.Bake);
            if (ClipValidation.HasErrors(validationMessages))
            {
                return false;
            }

            BlobAssetReference<ClipRegistryBlob> probeRegistry =
                BuildValidatedBlob(rig, canonicalClipSets, Allocator.Temp);
            try
            {
                contentHash = ComposeDedupKey(
                    HashRegistry(probeRegistry), ComposeBindKey(rig, canonicalClipSets));
            }
            finally
            {
                probeRegistry.Dispose();
            }
            return true;
        }

        // XOR is commutative, so set order cannot reach the key — but the list is canonicalised
        // before it gets here anyway, since a repeated set would otherwise cancel itself out of the fold.
        /// <summary>The bind's identity: the rig's stable id XOR-folded with every bound set's. Stamped into <see cref="ClipRegistryBlob.setKey"/> and folded again into the dedup key.</summary>
        internal static ulong ComposeBindKey(RigAsset rig, List<ClipSetAsset> canonicalClipSets)
        {
            ulong bindKey = rig == null ? 0UL : rig.StableId;
            for (int setIndex = 0; setIndex < canonicalClipSets.Count; setIndex++)
            {
                bindKey ^= canonicalClipSets[setIndex].stableId;
            }
            return bindKey;
        }

        /// <summary>
        /// Canonicalises a bind's set list: nulls dropped, repeats dropped by asset identity, sorted
        /// ascending by set stable id — so nothing downstream can depend on the order the sets were
        /// dragged into an inspector list.
        /// </summary>
        internal static List<ClipSetAsset> BuildCanonicalClipSets(IReadOnlyList<ClipSetAsset> clipSets)
        {
            List<ClipSetAsset> canonicalClipSets = new List<ClipSetAsset>();
            if (clipSets == null)
            {
                return canonicalClipSets;
            }

            HashSet<ClipSetAsset> seenClipSets = new HashSet<ClipSetAsset>();
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                if (clipSet == null || !seenClipSets.Add(clipSet))
                {
                    continue;
                }
                canonicalClipSets.Add(clipSet);
            }
            canonicalClipSets.Sort(CompareClipSetsByStableId);
            return canonicalClipSets;
        }

        // -----------------------------------------------------------------------------------
        // Validation gate and blob assembly.
        // -----------------------------------------------------------------------------------

        private static void ValidateForBakeOrThrow(RigAsset rig, List<ClipSetAsset> canonicalClipSets)
        {
            // ValidationStage.Bake selects the severity of the VAT-staleness check only. That check
            // needs a freshly recomputed VAT source hash, which requires the editor-only VAT baker
            // this assembly cannot reference, so it stays silent here and is judged in the Editor assembly.
            List<ValidationMessage> validationMessages =
                ClipValidation.ValidateBind(rig, canonicalClipSets, ValidationStage.Bake);
            if (ClipValidation.HasErrors(validationMessages))
            {
                throw new ClipValidationException(validationMessages);
            }
        }

        private static BlobAssetReference<ClipRegistryBlob> BuildValidatedBlob(
            RigAsset rig,
            List<ClipSetAsset> canonicalClipSets,
            AllocatorManager.AllocatorHandle allocator)
        {
            // Validation guarantees a rig with 1..8 layers, unique target ids, and clip ids unique
            // across the whole union. A binding that does not resolve against this rig is not
            // guaranteed away — rules T2 and T6 make it a skip — so the fill paths below still
            // handle an unresolved track rather than trusting one.
            List<RigTargetDefinition> canonicalTargets = BuildCanonicalTargets(rig);
            Dictionary<uint, int> denseTargetIndexById = BuildDenseTargetIndexMap(canonicalTargets);
            Dictionary<uint, int> denseTargetIndexByTagId = BuildDenseTargetIndexByTagId(canonicalTargets, denseTargetIndexById);
            List<ClipAsset> canonicalClips = BuildCanonicalClips(canonicalClipSets);
            VatTextureSetAsset vatTextures = ResolveBindVatTextures(canonicalClipSets);

            return BuildBlob(
                rig, canonicalClipSets, vatTextures, canonicalTargets, denseTargetIndexById,
                denseTargetIndexByTagId, canonicalClips, allocator);
        }

        /// <summary>
        /// The one VAT texture set a bind addresses. Rule V39 makes two of them an error, so by the
        /// time a validated bind reaches here there is at most one to find.
        /// </summary>
        private static VatTextureSetAsset ResolveBindVatTextures(List<ClipSetAsset> canonicalClipSets)
        {
            for (int setIndex = 0; setIndex < canonicalClipSets.Count; setIndex++)
            {
                if (canonicalClipSets[setIndex].vatTextures != null)
                {
                    return canonicalClipSets[setIndex].vatTextures;
                }
            }
            return null;
        }

        // -----------------------------------------------------------------------------------
        // Canonical ordering.
        // -----------------------------------------------------------------------------------

        private static List<RigTargetDefinition> BuildCanonicalTargets(RigAsset rig)
        {
            List<RigTargetDefinition> canonicalTargets = new List<RigTargetDefinition>();
            if (rig.targets != null)
            {
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                    if (targetDefinition != null)
                    {
                        canonicalTargets.Add(targetDefinition);
                    }
                }
            }
            canonicalTargets.Sort(CompareTargetsByStableId);
            return canonicalTargets;
        }

        private static Dictionary<uint, int> BuildDenseTargetIndexMap(
            List<RigTargetDefinition> canonicalTargets)
        {
            Dictionary<uint, int> denseTargetIndexById = new Dictionary<uint, int>(canonicalTargets.Count);
            for (int denseIndex = 0; denseIndex < canonicalTargets.Count; denseIndex++)
            {
                denseTargetIndexById[canonicalTargets[denseIndex].stableId] = denseIndex;
            }
            return denseTargetIndexById;
        }

        // Values are the same dense indices denseTargetIndexById already computed, just keyed
        // differently — no second sort. Rule V34 guarantees at most one target per rig carries a
        // given non-zero tag id, and ValidateForBakeOrThrow runs before this, so there is never a
        // winner to silently pick between two competing targets.
        /// <summary>Maps a tag id to the dense index of the one rig target carrying it.</summary>
        private static Dictionary<uint, int> BuildDenseTargetIndexByTagId(
            List<RigTargetDefinition> canonicalTargets, Dictionary<uint, int> denseTargetIndexById)
        {
            Dictionary<uint, int> denseTargetIndexByTagId = new Dictionary<uint, int>();
            for (int denseIndex = 0; denseIndex < canonicalTargets.Count; denseIndex++)
            {
                uint tagId = canonicalTargets[denseIndex].tagId;
                if (tagId != 0u)
                {
                    denseTargetIndexByTagId[tagId] = denseTargetIndexById[canonicalTargets[denseIndex].stableId];
                }
            }
            return denseTargetIndexByTagId;
        }

        /// <summary>The union of every bound set's clips, deduplicated by asset identity and sorted ascending by clip id — the position in that order is the dense clip index.</summary>
        private static List<ClipAsset> BuildCanonicalClips(List<ClipSetAsset> canonicalClipSets)
        {
            List<ClipAsset> canonicalClips = new List<ClipAsset>();
            // Dedup by asset identity. UnityEngine.Object overrides Equals/GetHashCode to compare
            // instances, so the set itself carries the identity semantics — no instance-id call.
            HashSet<ClipAsset> seenClips = new HashSet<ClipAsset>();
            for (int setIndex = 0; setIndex < canonicalClipSets.Count; setIndex++)
            {
                List<ClipAsset> clips = canonicalClipSets[setIndex].clips;
                if (clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    ClipAsset clip = clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }
                    // Rule V11: a clip registered twice — in one set or across two — contributes
                    // exactly one baked entry.
                    if (!seenClips.Add(clip))
                    {
                        continue;
                    }
                    canonicalClips.Add(clip);
                }
            }
            canonicalClips.Sort(CompareClipsByStableId);
            return canonicalClips;
        }

        // These two comparators carry no authoring-order tie-break, unlike the ones below, and they
        // do not need one: duplicate target ids inside a rig and duplicate clip ids inside a set are
        // rejected by validation, and Build runs that gate first, so the ids being compared are
        // unique and the comparison is already a total order. Without that gate an equal-id pair
        // would be ordered arbitrarily by List<T>.Sort's unstable introsort and the bake would stop
        // being deterministic — the dependency is real, which is why it is written down here.

        private static int CompareTargetsByStableId(RigTargetDefinition left, RigTargetDefinition right)
        {
            return left.stableId.CompareTo(right.stableId);
        }

        private static int CompareClipsByStableId(ClipAsset left, ClipAsset right)
        {
            return left.stableId.CompareTo(right.stableId);
        }

        private static int CompareClipSetsByStableId(ClipSetAsset left, ClipSetAsset right)
        {
            return left.stableId.CompareTo(right.stableId);
        }

        // -----------------------------------------------------------------------------------
        // Blob construction.
        // -----------------------------------------------------------------------------------

        private static BlobAssetReference<ClipRegistryBlob> BuildBlob(
            RigAsset rig,
            List<ClipSetAsset> canonicalClipSets,
            VatTextureSetAsset vatTextures,
            List<RigTargetDefinition> canonicalTargets,
            Dictionary<uint, int> denseTargetIndexById,
            Dictionary<uint, int> denseTargetIndexByTagId,
            List<ClipAsset> canonicalClips,
            AllocatorManager.AllocatorHandle allocator)
        {
            float3[] targetBoundsExtents = new float3[canonicalTargets.Count];
            for (int denseIndex = 0; denseIndex < canonicalTargets.Count; denseIndex++)
            {
                targetBoundsExtents[denseIndex] =
                    math.max(canonicalTargets[denseIndex].boundsExtents, float3.zero);
            }

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ClipRegistryBlob registryRoot = ref builder.ConstructRoot<ClipRegistryBlob>();
                registryRoot.schemaVersion = SchemaVersion;
                registryRoot.setKey = ComposeBindKey(rig, canonicalClipSets);
                registryRoot.vatSetKey = vatTextures == null ? 0UL : vatTextures.setKey;
                registryRoot.vatInfo = BuildVatInfo(vatTextures);

                BlobBuilderArray<uint> sortedTargetIdArray =
                    builder.Allocate(ref registryRoot.sortedTargetIds, canonicalTargets.Count);
                BlobBuilderArray<float3> targetBoundsExtentsArray =
                    builder.Allocate(ref registryRoot.targetBoundsExtents, canonicalTargets.Count);
                BlobBuilderArray<int> targetFramesPerVariantArray =
                    builder.Allocate(ref registryRoot.targetFramesPerVariant, canonicalTargets.Count);
                for (int denseIndex = 0; denseIndex < canonicalTargets.Count; denseIndex++)
                {
                    sortedTargetIdArray[denseIndex] = canonicalTargets[denseIndex].stableId;
                    targetBoundsExtentsArray[denseIndex] = targetBoundsExtents[denseIndex];

                    // Clamped rather than trusted: 0 or a negative would make the wrap divide by
                    // zero or produce a negative block base, and [Min(1)] only guards the inspector.
                    targetFramesPerVariantArray[denseIndex] =
                        math.max(1, canonicalTargets[denseIndex].framesPerVariant);
                }

                BlobBuilderArray<ClipBlob> clipArray =
                    builder.Allocate(ref registryRoot.clips, canonicalClips.Count);
                for (int denseClipIndex = 0; denseClipIndex < canonicalClips.Count; denseClipIndex++)
                {
                    FillClip(
                        ref builder,
                        ref clipArray[denseClipIndex],
                        canonicalClips[denseClipIndex],
                        rig,
                        vatTextures,
                        denseTargetIndexById,
                        denseTargetIndexByTagId,
                        targetBoundsExtents);
                }

                FillSortedClipIds(ref builder, ref registryRoot, canonicalClips);

                return builder.CreateBlobAssetReference<ClipRegistryBlob>(allocator);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static VatTextureInfoBlob BuildVatInfo(VatTextureSetAsset vatTextures)
        {
            // These numbers describe only the set's untargeted part; a per-part set with no
            // untargeted entry reports 0 / 1 / 0 so a consumer dividing by rowsPerFrame cannot trip.
            if (vatTextures == null || !vatTextures.TryGetPart(0u, out VatPartTextures untargetedPart))
            {
                return new VatTextureInfoBlob
                {
                    flavor = vatTextures == null ? VatFlavor.BoneMatrix : vatTextures.flavor,
                    textureWidth = 0,
                    rowsPerFrame = 1,
                    boneOrVertexCount = 0
                };
            }
            return new VatTextureInfoBlob
            {
                flavor = vatTextures.flavor,
                textureWidth = untargetedPart.textureWidth,
                rowsPerFrame = untargetedPart.rowsPerFrame,
                boneOrVertexCount = vatTextures.flavor == VatFlavor.BoneMatrix
                    ? untargetedPart.boneCount
                    : untargetedPart.vertexCount
            };
        }

        /// <summary>
        /// Writes the binary-search key array. Since <paramref name="canonicalClips"/> is already
        /// sorted ascending by clip id and <see cref="ClipRegistryBlob.clips"/> was filled from it
        /// in that order, this array is simply those ids in the same positions — which is why
        /// <c>ClipRegistryApi.TryResolveClip</c> can return the search position as the dense index.
        /// </summary>
        private static void FillSortedClipIds(
            ref BlobBuilder builder,
            ref ClipRegistryBlob registryRoot,
            List<ClipAsset> canonicalClips)
        {
            int clipCount = canonicalClips.Count;
            BlobBuilderArray<ulong> sortedClipIdArray =
                builder.Allocate(ref registryRoot.sortedClipIds, clipCount);
            for (int denseClipIndex = 0; denseClipIndex < clipCount; denseClipIndex++)
            {
                sortedClipIdArray[denseClipIndex] = canonicalClips[denseClipIndex].stableId;
            }
        }

        private static void FillClip(
            ref BlobBuilder builder,
            ref ClipBlob clipBlob,
            ClipAsset clip,
            RigAsset rig,
            VatTextureSetAsset vatTextures,
            Dictionary<uint, int> denseTargetIndexById,
            Dictionary<uint, int> denseTargetIndexByTagId,
            float3[] targetBoundsExtents)
        {
            clipBlob.clipId = clip.stableId;

            // Truncation is intentional: debugName is a 61-byte log/inspector label, never a key.
            FixedString64Bytes debugName = default;
            debugName.CopyFromTruncated(clip.name);
            clipBlob.debugName = debugName;

            clipBlob.duration = clip.duration;
            // The baked default is always a resolved mode: UseClipDefault is a command-side
            // sentinel and would be circular here, so it resolves to Once.
            clipBlob.defaultLoop = clip.defaultLoop == LoopMode.UseClipDefault
                ? LoopMode.Once
                : clip.defaultLoop;
            // Rule V12: blend defaults longer than the clip are warned about and clamped here.
            clipBlob.defaultBlendIn = math.clamp(clip.defaultBlendIn, 0f, clip.duration);
            clipBlob.defaultBlendOut = math.clamp(clip.defaultBlendOut, 0f, clip.duration);

            List<TransformTrackEntry> transformTrackEntries =
                BuildTransformTrackEntries(clip, rig, denseTargetIndexById, denseTargetIndexByTagId);
            List<SpriteTrackEntry> spriteTrackEntries =
                BuildSpriteTrackEntries(clip, rig, denseTargetIndexById, denseTargetIndexByTagId);

            FillTransformTracks(ref builder, ref clipBlob, transformTrackEntries);
            FillSpriteTracks(ref builder, ref clipBlob, spriteTrackEntries);
            FillEvents(ref builder, ref clipBlob, clip);

            // Pre-initialised so the short-circuited null case leaves a defined value behind.
            VatClipRange bakedRange = default;
            bool hasVatRange = vatTextures != null &&
                               vatTextures.TryGetClipRange(clip.stableId, out bakedRange);
            clipBlob.vatFrameStart = hasVatRange ? bakedRange.frameStart : -1;
            clipBlob.vatFrameCount = hasVatRange ? bakedRange.frameCount : 0;
            clipBlob.vatFps = hasVatRange ? bakedRange.fps : 0f;

            FillVatTargetRanges(ref builder, ref clipBlob, clip, vatTextures, denseTargetIndexById);
            FillBillboardTracks(ref builder, ref clipBlob, clip);

            clipBlob.offsetBounds = ComputeOffsetBounds(
                transformTrackEntries,
                targetBoundsExtents,
                hasVatRange,
                bakedRange);
        }

        // -----------------------------------------------------------------------------------
        // Track / key / event canonicalisation.
        // -----------------------------------------------------------------------------------

        private struct TransformTrackEntry
        {
            public int denseTargetIndex;
            public int authoringIndex;
            public TransformTrack track;
        }

        private struct SpriteTrackEntry
        {
            public int denseTargetIndex;
            public int authoringIndex;
            public SpriteTrack track;
        }

        private struct BillboardTrackEntry
        {
            public uint rootId;
            public int authoringIndex;
            public BillboardTrack track;
        }

        private struct EventEntry
        {
            public float normalizedTime;
            public int authoringIndex;
            public EventMarker marker;
        }

        private static List<TransformTrackEntry> BuildTransformTrackEntries(
            ClipAsset clip,
            RigAsset rig,
            Dictionary<uint, int> denseTargetIndexById,
            Dictionary<uint, int> denseTargetIndexByTagId)
        {
            List<TransformTrackEntry> entries = new List<TransformTrackEntry>();
            if (clip.transformTracks == null)
            {
                return entries;
            }
            for (int authoringIndex = 0; authoringIndex < clip.transformTracks.Count; authoringIndex++)
            {
                TransformTrack track = clip.transformTracks[authoringIndex];
                if (track == null)
                {
                    continue;
                }
                int denseTargetIndex;
                if (!TryResolveTrackBinding(
                    track.targetId, track.tagId, denseTargetIndexById, denseTargetIndexByTagId,
                    out denseTargetIndex))
                {
                    ReportUnresolvedTrackBinding(
                        clip, rig, track.targetId, track.tagId, "Transform track", authoringIndex);
                    continue;
                }
                entries.Add(new TransformTrackEntry
                {
                    denseTargetIndex = denseTargetIndex,
                    authoringIndex = authoringIndex,
                    track = track
                });
            }
            entries.Sort(CompareTransformTrackEntries);
            return entries;
        }

        private static List<SpriteTrackEntry> BuildSpriteTrackEntries(
            ClipAsset clip,
            RigAsset rig,
            Dictionary<uint, int> denseTargetIndexById,
            Dictionary<uint, int> denseTargetIndexByTagId)
        {
            List<SpriteTrackEntry> entries = new List<SpriteTrackEntry>();
            if (clip.spriteTracks == null)
            {
                return entries;
            }
            for (int authoringIndex = 0; authoringIndex < clip.spriteTracks.Count; authoringIndex++)
            {
                SpriteTrack track = clip.spriteTracks[authoringIndex];
                if (track == null)
                {
                    continue;
                }
                int denseTargetIndex;
                if (!TryResolveTrackBinding(
                    track.targetId, track.tagId, denseTargetIndexById, denseTargetIndexByTagId,
                    out denseTargetIndex))
                {
                    ReportUnresolvedTrackBinding(
                        clip, rig, track.targetId, track.tagId, "Sprite track", authoringIndex);
                    continue;
                }
                entries.Add(new SpriteTrackEntry
                {
                    denseTargetIndex = denseTargetIndex,
                    authoringIndex = authoringIndex,
                    track = track
                });
            }
            entries.Sort(CompareSpriteTrackEntries);
            return entries;
        }

        /// <summary>Resolves one track's binding to a dense target index: by tag when <paramref name="tagId"/> is non-zero, by <paramref name="targetId"/> otherwise.</summary>
        /// <returns>False when neither id resolves — the caller must report and skip the track rather than add an entry.</returns>
        private static bool TryResolveTrackBinding(
            uint targetId,
            uint tagId,
            Dictionary<uint, int> denseTargetIndexById,
            Dictionary<uint, int> denseTargetIndexByTagId,
            out int denseTargetIndex)
        {
            if (tagId != 0u)
            {
                return denseTargetIndexByTagId.TryGetValue(tagId, out denseTargetIndex);
            }
            return denseTargetIndexById.TryGetValue(targetId, out denseTargetIndex);
        }

        /// <summary>Reports a track whose binding did not resolve — names the tag for a tag-bound track and the target id for an id-bound one, since those are the two things an author would go fix.</summary>
        private static void ReportUnresolvedTrackBinding(
            ClipAsset clip,
            RigAsset rig,
            uint targetId,
            uint tagId,
            string trackKindLabel,
            int authoringIndex)
        {
            string rigName = rig != null ? rig.name : "(no rig)";
            string clipName = clip != null ? clip.name : "(no clip)";
            string bindingDescription = tagId != 0u
                ? "binds tag id 0x" + tagId.ToString("X8") + ", which rig '" + rigName +
                  "' has no target for; the track is skipped in this bake (rule T2)."
                : "targets id 0x" + targetId.ToString("X8") + ", which rig '" + rigName +
                  "' does not declare; the track is skipped in this bake (rule T6).";
            Debug.LogWarning(
                "[DOTS Animation Toolkit] " + trackKindLabel + " " + authoringIndex + " of clip '" +
                clipName + "' " + bindingDescription);
        }

        private static int CompareTransformTrackEntries(TransformTrackEntry left, TransformTrackEntry right)
        {
            int targetOrder = left.denseTargetIndex.CompareTo(right.denseTargetIndex);
            return targetOrder != 0 ? targetOrder : left.authoringIndex.CompareTo(right.authoringIndex);
        }

        private static int CompareSpriteTrackEntries(SpriteTrackEntry left, SpriteTrackEntry right)
        {
            int targetOrder = left.denseTargetIndex.CompareTo(right.denseTargetIndex);
            return targetOrder != 0 ? targetOrder : left.authoringIndex.CompareTo(right.authoringIndex);
        }

        // Sorted by id rather than a dense index, since billboard roots have no dense array to
        // index into — an id is a stable, machine-independent number, which is all determinism needs.
        /// <summary>Canonical order for billboard tracks: ascending root id, ties broken by authoring order.</summary>
        private static int CompareBillboardTrackEntries(
            BillboardTrackEntry left, BillboardTrackEntry right)
        {
            int rootOrder = left.rootId.CompareTo(right.rootId);
            return rootOrder != 0 ? rootOrder : left.authoringIndex.CompareTo(right.authoringIndex);
        }

        private static int CompareEventEntries(EventEntry left, EventEntry right)
        {
            int timeOrder = left.normalizedTime.CompareTo(right.normalizedTime);
            return timeOrder != 0 ? timeOrder : left.authoringIndex.CompareTo(right.authoringIndex);
        }

        // Keys are copied in authoring order and never re-sorted. Rule V03 makes a track whose keys
        // are not strictly ascending in normalizedTime an Error, and Build throws on any error
        // before reaching here, so authoring order and canonical order are the same order for every
        // clip that can be baked. Events are different: no rule constrains marker order, so the
        // event list below really is sorted at bake.

        private static void FillTransformTracks(
            ref BlobBuilder builder,
            ref ClipBlob clipBlob,
            List<TransformTrackEntry> transformTrackEntries)
        {
            BlobBuilderArray<TransformTrackBlob> trackArray =
                builder.Allocate(ref clipBlob.transformTracks, transformTrackEntries.Count);
            for (int trackIndex = 0; trackIndex < transformTrackEntries.Count; trackIndex++)
            {
                TransformTrackEntry entry = transformTrackEntries[trackIndex];
                trackArray[trackIndex].targetIndex = entry.denseTargetIndex;
                trackArray[trackIndex].blendOp = entry.track.blendOp;
                trackArray[trackIndex].channels = entry.track.channels;

                int keyCount = entry.track.keys == null ? 0 : entry.track.keys.Count;
                BlobBuilderArray<TransformKeyBlob> keyArray =
                    builder.Allocate(ref trackArray[trackIndex].keys, keyCount);
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    TransformKey authoredKey = entry.track.keys[keyIndex];
                    keyArray[keyIndex] = new TransformKeyBlob
                    {
                        normalizedTime = authoredKey.normalizedTime,
                        position = authoredKey.position,
                        // Section 4.5 point 2: degrees in, radians out, converted once at bake.
                        rotation = math.radians(authoredKey.rotation),
                        scale = authoredKey.scale,
                        interpolation = authoredKey.interpolation,
                        bezierStartHandle = authoredKey.bezierStartHandle,
                        bezierEndHandle = authoredKey.bezierEndHandle
                    };
                }
            }
        }

        private static void FillSpriteTracks(
            ref BlobBuilder builder,
            ref ClipBlob clipBlob,
            List<SpriteTrackEntry> spriteTrackEntries)
        {
            BlobBuilderArray<SpriteTrackBlob> trackArray =
                builder.Allocate(ref clipBlob.spriteTracks, spriteTrackEntries.Count);
            for (int trackIndex = 0; trackIndex < spriteTrackEntries.Count; trackIndex++)
            {
                SpriteTrackEntry entry = spriteTrackEntries[trackIndex];
                trackArray[trackIndex].targetIndex = entry.denseTargetIndex;
                trackArray[trackIndex].mode = entry.track.mode;
                trackArray[trackIndex].sliceSpace = entry.track.sliceSpace;
                trackArray[trackIndex].baseIndex = entry.track.baseIndex;

                int keyCount = entry.track.keys == null ? 0 : entry.track.keys.Count;
                BlobBuilderArray<SpriteKeyBlob> keyArray =
                    builder.Allocate(ref trackArray[trackIndex].keys, keyCount);
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    SpriteKey authoredKey = entry.track.keys[keyIndex];
                    keyArray[keyIndex] = new SpriteKeyBlob
                    {
                        normalizedTime = authoredKey.normalizedTime,
                        sliceIndex = authoredKey.sliceIndex,
                        indexMode = authoredKey.indexMode,
                        atlasRect = authoredKey.atlasRect
                    };
                }
            }
        }

        /// <summary>Bakes the clip's billboard tracks, in canonical root-id order.</summary>
        private static void FillBillboardTracks(
            ref BlobBuilder builder, ref ClipBlob clipBlob, ClipAsset clip)
        {
            List<BillboardTrackEntry> entries = new List<BillboardTrackEntry>();
            if (clip.billboardTracks != null)
            {
                for (int authoringIndex = 0;
                     authoringIndex < clip.billboardTracks.Count;
                     authoringIndex++)
                {
                    BillboardTrack track = clip.billboardTracks[authoringIndex];
                    if (track == null)
                    {
                        continue;
                    }
                    entries.Add(new BillboardTrackEntry
                    {
                        rootId = track.rootStableId,
                        authoringIndex = authoringIndex,
                        track = track
                    });
                }
            }
            entries.Sort(CompareBillboardTrackEntries);

            BlobBuilderArray<BillboardTrackBlob> trackArray =
                builder.Allocate(ref clipBlob.billboardTracks, entries.Count);
            for (int trackIndex = 0; trackIndex < entries.Count; trackIndex++)
            {
                BillboardTrackEntry entry = entries[trackIndex];
                trackArray[trackIndex].rootId = entry.rootId;

                int keyCount = entry.track.keys == null ? 0 : entry.track.keys.Count;
                BlobBuilderArray<BillboardKeyBlob> keyArray =
                    builder.Allocate(ref trackArray[trackIndex].keys, keyCount);
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    BillboardKey authoredKey = entry.track.keys[keyIndex];
                    keyArray[keyIndex] = new BillboardKeyBlob
                    {
                        normalizedTime = authoredKey.normalizedTime,
                        // Degrees are what an author types; radians are what trigonometry consumes.
                        angleOffsetRadians = math.radians(authoredKey.angleOffsetDegrees),
                        blendWeight = math.saturate(authoredKey.blendWeight),
                        enabled = authoredKey.enabled,
                        interpolation = authoredKey.interpolation,
                        bezierStartHandle = authoredKey.bezierStartHandle,
                        bezierEndHandle = authoredKey.bezierEndHandle
                    };
                }
            }
        }

        private static void FillEvents(ref BlobBuilder builder, ref ClipBlob clipBlob, ClipAsset clip)
        {
            List<EventEntry> eventEntries = new List<EventEntry>();
            if (clip.events != null)
            {
                for (int eventIndex = 0; eventIndex < clip.events.Count; eventIndex++)
                {
                    eventEntries.Add(new EventEntry
                    {
                        normalizedTime = clip.events[eventIndex].normalizedTime,
                        authoringIndex = eventIndex,
                        marker = clip.events[eventIndex]
                    });
                }
            }
            eventEntries.Sort(CompareEventEntries);

            BlobBuilderArray<EventMarkerBlob> eventArray =
                builder.Allocate(ref clipBlob.events, eventEntries.Count);
            for (int eventIndex = 0; eventIndex < eventEntries.Count; eventIndex++)
            {
                EventMarker authoredMarker = eventEntries[eventIndex].marker;
                eventArray[eventIndex] = new EventMarkerBlob
                {
                    normalizedTime = authoredMarker.normalizedTime,
                    eventKey = authoredMarker.eventKey,
                    intParam = authoredMarker.intParam,
                    floatParam = authoredMarker.floatParam,

                    // Clamped rather than trusted: a negative window is a validation error (V19),
                    // but the bake must still produce a blob a job can read without checking, and
                    // a negative duration would make every window test answer false in a way that
                    // looks like the marker was never authored at all.
                    windowSeconds = math.max(0f, authoredMarker.windowSeconds)
                };
            }
        }

        // -----------------------------------------------------------------------------------
        // Per-target VAT ranges.
        // -----------------------------------------------------------------------------------

        private struct VatTargetRangeEntry
        {
            public int denseTargetIndex;
            public int authoringIndex;
            public VatClipRange range;
        }

        // The baked texture set, not ClipAsset.vatTracks, is the source of truth: vatTracks is
        // authoring intent, vatTextures.clipRanges is what was actually baked. Rule V07 guarantees
        // every track with a source clip has a matching exact range by the time this runs, so
        // reading the baked ranges cannot silently drop a track.
        // A range naming a target id the current rig does not declare (stale, from a removed
        // target) is skipped rather than thrown on — validation already reported this rig/clip
        // combination as broken elsewhere.
        /// <summary>
        /// Fills <see cref="ClipBlob.vatTargetRanges"/> from every exact (clip, target) range
        /// <paramref name="vatTextures"/> holds for <paramref name="clip"/>.
        /// </summary>
        private static void FillVatTargetRanges(
            ref BlobBuilder builder,
            ref ClipBlob clipBlob,
            ClipAsset clip,
            VatTextureSetAsset vatTextures,
            Dictionary<uint, int> denseTargetIndexById)
        {
            List<VatTargetRangeEntry> entries = new List<VatTargetRangeEntry>();
            if (vatTextures != null && vatTextures.clipRanges != null)
            {
                for (int rangeIndex = 0; rangeIndex < vatTextures.clipRanges.Count; rangeIndex++)
                {
                    VatClipRange candidate = vatTextures.clipRanges[rangeIndex];
                    if (candidate.clipId != clip.stableId || candidate.targetId == 0u)
                    {
                        continue;
                    }

                    int denseTargetIndex;
                    if (!denseTargetIndexById.TryGetValue(candidate.targetId, out denseTargetIndex))
                    {
                        continue;
                    }

                    entries.Add(new VatTargetRangeEntry
                    {
                        denseTargetIndex = denseTargetIndex,
                        authoringIndex = rangeIndex,
                        range = candidate
                    });
                }
            }
            entries.Sort(CompareVatTargetRangeEntries);

            BlobBuilderArray<VatTrackRangeBlob> rangeArray =
                builder.Allocate(ref clipBlob.vatTargetRanges, entries.Count);
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                VatTargetRangeEntry entry = entries[entryIndex];
                rangeArray[entryIndex] = new VatTrackRangeBlob
                {
                    targetIndex = entry.denseTargetIndex,
                    frameStart = entry.range.frameStart,
                    frameCount = entry.range.frameCount,
                    fps = entry.range.fps
                };
            }
        }

        private static int CompareVatTargetRangeEntries(VatTargetRangeEntry left, VatTargetRangeEntry right)
        {
            int targetOrder = left.denseTargetIndex.CompareTo(right.denseTargetIndex);
            return targetOrder != 0 ? targetOrder : left.authoringIndex.CompareTo(right.authoringIndex);
        }

        // -----------------------------------------------------------------------------------
        // Bounds at bake.
        // -----------------------------------------------------------------------------------

        // Result is in offset space, not actor space: keys are local offsets from a target's rest
        // pose, and rest poses live on the actor prefab, which this builder never sees, so every
        // contributing box is centred on the origin. Entity baking combines this with the rest
        // poses to get an actor-space box.
        /// <summary>
        /// Unions every transform key's position offset grown by its target's authored half-extents,
        /// the origin-centred rest box of every target no key moved, and the exact measured bounds
        /// of the clip's VAT range when it has one. Conservative by construction, since every
        /// interpolation mode is monotonic between keys.
        /// </summary>
        /// <returns>The conservative offset-space clip bounds, or a zero box when the rig declares no targets.</returns>
        private static AABB ComputeOffsetBounds(
            List<TransformTrackEntry> transformTrackEntries,
            float3[] targetBoundsExtents,
            bool hasVatRange,
            VatClipRange bakedRange)
        {
            MinMaxAABB unionBounds = MinMaxAABB.Empty;
            bool[] targetKeyed = new bool[targetBoundsExtents.Length];

            for (int trackIndex = 0; trackIndex < transformTrackEntries.Count; trackIndex++)
            {
                TransformTrackEntry entry = transformTrackEntries[trackIndex];
                if (entry.track.keys == null || entry.track.keys.Count == 0)
                {
                    continue;
                }
                float3 targetExtents = targetBoundsExtents[entry.denseTargetIndex];
                targetKeyed[entry.denseTargetIndex] = true;
                for (int keyIndex = 0; keyIndex < entry.track.keys.Count; keyIndex++)
                {
                    TransformKey authoredKey = entry.track.keys[keyIndex];
                    // All three axes now, and the key's z offset is part of the box rather than
                    // flattened to 0: a 3D rig moves in depth, and a bound that ignored z would cull
                    // a vehicle the moment it drove away from the origin plane.
                    float scaleFactor = math.max(
                        math.cmax(math.abs(authoredKey.scale)),
                        1f);
                    float3 scaledExtents = targetExtents * scaleFactor;
                    float3 keyCenter = authoredKey.position;
                    unionBounds.Encapsulate(keyCenter - scaledExtents);
                    unionBounds.Encapsulate(keyCenter + scaledExtents);
                }
            }

            for (int denseIndex = 0; denseIndex < targetBoundsExtents.Length; denseIndex++)
            {
                if (targetKeyed[denseIndex])
                {
                    continue;
                }
                unionBounds.Encapsulate(-targetBoundsExtents[denseIndex]);
                unionBounds.Encapsulate(targetBoundsExtents[denseIndex]);
            }

            if (hasVatRange)
            {
                UnityEngine.Vector3 vatMin = bakedRange.bounds.min;
                UnityEngine.Vector3 vatMax = bakedRange.bounds.max;
                unionBounds.Encapsulate(new float3(vatMin.x, vatMin.y, vatMin.z));
                unionBounds.Encapsulate(new float3(vatMax.x, vatMax.y, vatMax.z));
            }

            if (unionBounds.IsEmpty)
            {
                return new AABB { Center = float3.zero, Extents = float3.zero };
            }
            return unionBounds;
        }

        // -----------------------------------------------------------------------------------
        // Content hash.
        // -----------------------------------------------------------------------------------

        private static Unity.Entities.Hash128 ComposeDedupKey(ulong contentHash64, ulong bindKey)
        {
            return new Unity.Entities.Hash128(
                (uint)contentHash64,
                (uint)(contentHash64 >> 32),
                (uint)SchemaVersion,
                (uint)bindKey ^ (uint)(bindKey >> 32));
        }

        private static ulong HashRegistry(BlobAssetReference<ClipRegistryBlob> registry)
        {
            ref ClipRegistryBlob registryRoot = ref registry.Value;
            return ComputeContentHash(ref registryRoot);
        }

        // The finished blob is hashed, not the authoring graph, so the key cannot disagree with the
        // bytes it stands for even across this builder's own canonicalisation. Every field of the
        // blob is visited, and array lengths are hashed alongside their elements, so no edit
        // anywhere — a target id, a VAT parameter, a clip's debug name — can be answered from the
        // store with a stale blob.
        /// <summary>Hashes the finished blob over its canonical stream, with every float contributed as its <c>math.asuint</c> bit pattern so the hash is exact rather than rounded.</summary>
        /// <returns>The 64-bit content hash.</returns>
        private static ulong ComputeContentHash(ref ClipRegistryBlob registryRoot)
        {
            xxHash3.StreamingState hashState = new xxHash3.StreamingState(true);
            hashState.Update(registryRoot.schemaVersion);
            hashState.Update(registryRoot.setKey);
            hashState.Update(registryRoot.vatSetKey);

            hashState.Update(registryRoot.sortedTargetIds.Length);
            for (int denseTargetIndex = 0;
                 denseTargetIndex < registryRoot.sortedTargetIds.Length;
                 denseTargetIndex++)
            {
                hashState.Update(registryRoot.sortedTargetIds[denseTargetIndex]);
                float3 boundsExtents = registryRoot.targetBoundsExtents[denseTargetIndex];
                hashState.Update(math.asuint(boundsExtents.x));
                hashState.Update(math.asuint(boundsExtents.y));
                hashState.Update(math.asuint(boundsExtents.z));
            }

            hashState.Update((byte)registryRoot.vatInfo.flavor);
            hashState.Update(registryRoot.vatInfo.textureWidth);
            hashState.Update(registryRoot.vatInfo.rowsPerFrame);
            hashState.Update(registryRoot.vatInfo.boneOrVertexCount);

            hashState.Update(registryRoot.clips.Length);
            for (int denseClipIndex = 0; denseClipIndex < registryRoot.clips.Length; denseClipIndex++)
            {
                HashClip(ref hashState, ref registryRoot.clips[denseClipIndex]);
            }

            uint2 digest = hashState.DigestHash64();
            return ((ulong)digest.y << 32) | digest.x;
        }

        /// <summary>
        /// Appends one clip to the canonical stream: identity and debug name, timing and blend
        /// defaults, then the transform tracks, sprite tracks and events with their array lengths,
        /// then the VAT frame range and the offset-space bounds.
        /// </summary>
        private static void HashClip(ref xxHash3.StreamingState hashState, ref ClipBlob clipBlob)
        {
            hashState.Update(clipBlob.clipId);

            // debugName is baked content — it is what logs and the inspector show — so it belongs in
            // the key. Length first, then the UTF-8 bytes, so a name that is a prefix of another
            // cannot produce the same stream.
            hashState.Update(clipBlob.debugName.Length);
            for (int nameByteIndex = 0; nameByteIndex < clipBlob.debugName.Length; nameByteIndex++)
            {
                hashState.Update(clipBlob.debugName[nameByteIndex]);
            }

            hashState.Update(math.asuint(clipBlob.duration));
            hashState.Update((byte)clipBlob.defaultLoop);
            hashState.Update(math.asuint(clipBlob.defaultBlendIn));
            hashState.Update(math.asuint(clipBlob.defaultBlendOut));

            hashState.Update(clipBlob.transformTracks.Length);
            for (int trackIndex = 0; trackIndex < clipBlob.transformTracks.Length; trackIndex++)
            {
                ref TransformTrackBlob trackBlob = ref clipBlob.transformTracks[trackIndex];
                hashState.Update(trackBlob.targetIndex);
                hashState.Update((byte)trackBlob.blendOp);
                hashState.Update((byte)trackBlob.channels);
                hashState.Update(trackBlob.keys.Length);
                for (int keyIndex = 0; keyIndex < trackBlob.keys.Length; keyIndex++)
                {
                    ref TransformKeyBlob keyBlob = ref trackBlob.keys[keyIndex];
                    hashState.Update(math.asuint(keyBlob.normalizedTime));
                    hashState.Update(math.asuint(keyBlob.position.x));
                    hashState.Update(math.asuint(keyBlob.position.y));
                    hashState.Update(math.asuint(keyBlob.position.z));
                    hashState.Update(math.asuint(keyBlob.rotation.x));
                    hashState.Update(math.asuint(keyBlob.rotation.y));
                    hashState.Update(math.asuint(keyBlob.rotation.z));
                    hashState.Update(math.asuint(keyBlob.scale.x));
                    hashState.Update(math.asuint(keyBlob.scale.y));
                    hashState.Update(math.asuint(keyBlob.scale.z));
                    hashState.Update((byte)keyBlob.interpolation);
                    hashState.Update(math.asuint(keyBlob.bezierStartHandle.x));
                    hashState.Update(math.asuint(keyBlob.bezierStartHandle.y));
                    hashState.Update(math.asuint(keyBlob.bezierEndHandle.x));
                    hashState.Update(math.asuint(keyBlob.bezierEndHandle.y));
                }
            }

            hashState.Update(clipBlob.spriteTracks.Length);
            for (int trackIndex = 0; trackIndex < clipBlob.spriteTracks.Length; trackIndex++)
            {
                ref SpriteTrackBlob trackBlob = ref clipBlob.spriteTracks[trackIndex];
                hashState.Update(trackBlob.targetIndex);
                hashState.Update((byte)trackBlob.mode);

                // sliceSpace was previously absent from this stream, so two clips differing only in
                // absolute-vs-relative keys hashed identically.
                hashState.Update((byte)trackBlob.sliceSpace);
                hashState.Update(trackBlob.baseIndex);
                hashState.Update(trackBlob.keys.Length);
                for (int keyIndex = 0; keyIndex < trackBlob.keys.Length; keyIndex++)
                {
                    ref SpriteKeyBlob keyBlob = ref trackBlob.keys[keyIndex];
                    hashState.Update(math.asuint(keyBlob.normalizedTime));
                    hashState.Update(keyBlob.sliceIndex);
                    hashState.Update((byte)keyBlob.indexMode);
                    hashState.Update(math.asuint(keyBlob.atlasRect.x));
                    hashState.Update(math.asuint(keyBlob.atlasRect.y));
                    hashState.Update(math.asuint(keyBlob.atlasRect.z));
                    hashState.Update(math.asuint(keyBlob.atlasRect.w));
                }
            }

            hashState.Update(clipBlob.billboardTracks.Length);
            for (int trackIndex = 0; trackIndex < clipBlob.billboardTracks.Length; trackIndex++)
            {
                ref BillboardTrackBlob trackBlob = ref clipBlob.billboardTracks[trackIndex];
                hashState.Update(trackBlob.rootId);
                hashState.Update(trackBlob.keys.Length);
                for (int keyIndex = 0; keyIndex < trackBlob.keys.Length; keyIndex++)
                {
                    ref BillboardKeyBlob keyBlob = ref trackBlob.keys[keyIndex];
                    hashState.Update(math.asuint(keyBlob.normalizedTime));
                    hashState.Update(math.asuint(keyBlob.angleOffsetRadians));
                    hashState.Update(math.asuint(keyBlob.blendWeight));
                    hashState.Update((byte)(keyBlob.enabled ? 1 : 0));
                    hashState.Update((byte)keyBlob.interpolation);
                    hashState.Update(math.asuint(keyBlob.bezierStartHandle.x));
                    hashState.Update(math.asuint(keyBlob.bezierStartHandle.y));
                    hashState.Update(math.asuint(keyBlob.bezierEndHandle.x));
                    hashState.Update(math.asuint(keyBlob.bezierEndHandle.y));
                }
            }

            hashState.Update(clipBlob.events.Length);
            for (int eventIndex = 0; eventIndex < clipBlob.events.Length; eventIndex++)
            {
                ref EventMarkerBlob eventBlob = ref clipBlob.events[eventIndex];
                hashState.Update(math.asuint(eventBlob.normalizedTime));
                hashState.Update(eventBlob.eventKey);
                hashState.Update(eventBlob.intParam);
                hashState.Update(math.asuint(eventBlob.floatParam));
                hashState.Update(math.asuint(eventBlob.windowSeconds));
            }

            hashState.Update(clipBlob.vatFrameStart);
            hashState.Update(clipBlob.vatFrameCount);
            hashState.Update(math.asuint(clipBlob.vatFps));

            // Per-target VAT range overrides join the stream the same way every other array does —
            // length first, then each element — so two blobs differing only in which parts have a
            // dedicated baked range can never hash the same.
            hashState.Update(clipBlob.vatTargetRanges.Length);
            for (int rangeIndex = 0; rangeIndex < clipBlob.vatTargetRanges.Length; rangeIndex++)
            {
                ref VatTrackRangeBlob rangeBlob = ref clipBlob.vatTargetRanges[rangeIndex];
                hashState.Update(rangeBlob.targetIndex);
                hashState.Update(rangeBlob.frameStart);
                hashState.Update(rangeBlob.frameCount);
                hashState.Update(math.asuint(rangeBlob.fps));
            }

            hashState.Update(math.asuint(clipBlob.offsetBounds.Center.x));
            hashState.Update(math.asuint(clipBlob.offsetBounds.Center.y));
            hashState.Update(math.asuint(clipBlob.offsetBounds.Center.z));
            hashState.Update(math.asuint(clipBlob.offsetBounds.Extents.x));
            hashState.Update(math.asuint(clipBlob.offsetBounds.Extents.y));
            hashState.Update(math.asuint(clipBlob.offsetBounds.Extents.z));
        }
    }
}
