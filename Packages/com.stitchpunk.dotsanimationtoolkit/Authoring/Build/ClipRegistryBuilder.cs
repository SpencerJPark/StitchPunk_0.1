// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// Turns a <see cref="ClipSetAsset"/>'s ScriptableObject graph into the single
    /// <see cref="ClipRegistryBlob"/> the runtime reads (architecture sections 4.2, 4.5, 4.6),
    /// together with the content hash that keys it in the <c>BlobAssetStore</c>. The same builder
    /// serves entity baking and the editor's preview, so preview and runtime are structurally
    /// guaranteed to sample identical data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The build is a pure function of the source assets: given the same assets it produces the same
    /// blob and the same hash on every machine and in every session. Determinism comes from three
    /// places — a canonical ordering that discards authoring list order, canonical value conversions
    /// applied once at bake, and a content hash taken over a canonical byte stream of float
    /// <em>bit patterns</em> rather than formatted values (architecture section 4.5).
    /// </para>
    /// <para>
    /// The canonical order is: clips ascending by clip id, whose position in that order <em>is</em>
    /// the dense clip index; targets ascending by target id, whose position <em>is</em> the dense
    /// target index; tracks ascending by dense target index with authoring list order breaking ties
    /// and every track kept; events ascending by normalized time with authoring order breaking ties.
    /// Every comparison is therefore a total order, which is what makes the result independent of
    /// the sorting algorithm. Key order is not re-derived here — validation rule V03 makes any track
    /// whose keys are not strictly time-sorted an error, and <see cref="Build"/> runs that gate
    /// before it touches the rig, so keys reach the blob in the order they were authored in.
    /// </para>
    /// <para>
    /// The hashed stream covers every field of the finished blob (see
    /// <see cref="ComputeContentHash"/>), so a change anywhere in the baked bytes changes the dedup
    /// key that stands for them.
    /// </para>
    /// </remarks>
    public static class ClipRegistryBuilder
    {
        /// <summary>
        /// Layout version stamped into <see cref="ClipRegistryBlob.schemaVersion"/> and mixed into
        /// the dedup hash. Bump it whenever the blob layout or the canonical stream changes, so old
        /// baked scenes cannot be silently mis-read (architecture section 12, risk R5).
        /// </summary>
        /// <remarks>
        /// Version 2 (C2 rework): <c>clipIndexById</c> removed from the blob — clips are id-sorted,
        /// so the dense index is the binary-search position; <c>localBounds</c> renamed to
        /// <see cref="ClipBlob.offsetBounds"/> to name the frame it is actually computed in
        /// (architecture section 4.6); and the canonical hash stream widened to cover
        /// <see cref="ClipRegistryBlob.sortedTargetIds"/>,
        /// <see cref="ClipRegistryBlob.targetBoundsExtents"/>, every
        /// <see cref="VatTextureInfoBlob"/> field and <see cref="ClipBlob.debugName"/>.
        /// <para>
        /// Version 3: <c>VatClipRange</c> gained <c>targetId</c> and <see cref="ClipBlob"/> gained
        /// the (then-unfilled) <see cref="ClipBlob.vatTargetRanges"/> array, in preparation for
        /// multi-source VAT tracks (C10).
        /// </para>
        /// <para>
        /// Version 4 (C10, multi-source VAT tracks): <see cref="ClipBlob.vatTargetRanges"/> is
        /// actually filled from <c>ClipAsset.vatTracks</c> via the exact (clip, target) ranges
        /// <c>VatTextureSetAsset.clipRanges</c> holds, and the canonical hash stream widened to cover
        /// it — see <see cref="HashClip"/>. A registry baked under version 3 or earlier always has an
        /// empty <see cref="ClipBlob.vatTargetRanges"/> regardless of what a v4-and-later bake of the
        /// same assets would produce, so the bump is required: the same source bytes now produce
        /// different blob content depending on which builder version wrote them, and a stale
        /// version-3 blob must never be mistaken for a version-4 one that would resolve differently
        /// at runtime for any actor using <c>ClipAsset.vatTracks</c>.
        /// </para>
        /// <para>
        /// Version 5 adds per-key Bézier handles to transform keys, and a per-track
        /// <c>baseIndex</c> plus a per-key <see cref="SpriteIndexMode"/> to sprite tracks. A
        /// version-4 blob read as version 5 would resolve every relative flipbook key against a
        /// base of zero and ease every Bézier key as linear, so the two are not interchangeable.
        /// This version also puts <c>sliceSpace</c> into the content hash for the first time.
        /// </para>
        /// </remarks>
        public const int SchemaVersion = 5;

        /// <summary>
        /// Number of times <see cref="Build"/> has allocated a persistent blob this session.
        /// </summary>
        /// <remarks>
        /// The store-hit short-circuit in <c>ActorBaker.TryAcquireRegistry</c> has no observable
        /// effect on baked data: whether the baker probes and skips the build, or builds and lets
        /// <c>AddBlobAssetWithCustomHash</c> discard the duplicate, both actors end up referencing
        /// the same blob. The only difference is the work done. So if the probe ever stopped
        /// matching the key <see cref="Build"/> produces, every crowd would silently bake its
        /// registry once per actor and no assertion about entity data could notice. This counter is
        /// the seam that makes the difference assertable.
        /// </para>
        /// <para>
        /// Be precise about what the short-circuit saves, because the number is smaller than it
        /// looks: <see cref="TryComputeContentHash"/> runs the same canonicalisation and hash
        /// <see cref="Build"/> does, only into <see cref="Allocator.Temp"/>. Every actor therefore
        /// pays the canonicalisation pass whether or not it hits in the store. What a hit avoids is
        /// the <em>persistent</em> allocation and the store insert per duplicate actor — real, and
        /// the part that scales with crowd size in memory rather than in time.
        /// </remarks>
        /// <remarks>
        /// <para>
        /// <strong>Editor-only, and deliberately so.</strong> The Authoring assembly declares empty
        /// <c>includePlatforms</c>, which is the standard Entities authoring layout — bakers and SO
        /// classes compile for players. An unguarded counter would therefore exist in a shipped
        /// player and the public <see cref="Build"/> would mutate it there on every call, for a
        /// seam that only baking and editor preview can ever read. <c>internal</c> stops a consumer
        /// depending on it; it does not stop it shipping. <c>UNITY_EDITOR</c> does, and baking and
        /// preview are both editor-only, so nothing that can read it is lost.
        /// </para>
        /// <para>
        /// <c>Interlocked</c> rather than <c>++</c>: Entities invokes bakers from a single
        /// main-thread loop today, so the read-modify-write is not currently raced, but that is an
        /// implementation detail of a dependency rather than a contract, and <see cref="Build"/> is
        /// public — an editor tool may call it from anywhere. The atomic costs nothing here.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Builds the registry blob for a clip set, together with its <c>BlobAssetStore</c> dedup
        /// key.
        /// </summary>
        /// <param name="clipSet">The set to bake. Must pass validation with no errors.</param>
        /// <param name="registry">
        /// The built blob, allocated with <see cref="Allocator.Persistent"/>. <strong>Ownership
        /// passes to the caller.</strong> In entity baking the caller hands it straight to
        /// <c>Baker.AddBlobAssetWithCustomHash</c>, after which the <c>BlobAssetStore</c> owns it and
        /// nothing in the package disposes it by hand. A caller that builds a blob it does not hand
        /// to a store — an editor preview, a test — owns the disposal itself. To avoid building a
        /// blob only to discard it on a store hit, ask for the key first with
        /// <see cref="TryComputeContentHash"/>.
        /// </param>
        /// <param name="contentHash">
        /// The <c>BlobAssetStore</c> dedup key of architecture section 4.5: the 64-bit content hash
        /// in the low two words, the schema version, and the folded set key. Because the folded set
        /// key is part of the key and <see cref="ClipRegistryBlob.setKey"/> is part of the hashed
        /// stream, two <em>different</em> sets never dedup onto one blob even when their content is
        /// identical — deduplication is scoped to one set, which is what makes many actors sharing a
        /// <see cref="ClipSetAsset"/> share one blob and makes a rebake with unchanged content a
        /// no-op.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clipSet"/> is null.</exception>
        /// <exception cref="ClipValidationException">
        /// Thrown when the set carries any validation error (architecture section 3.5); the
        /// exception lists every offending rule code.
        /// </exception>
        public static void Build(
            ClipSetAsset clipSet,
            out BlobAssetReference<ClipRegistryBlob> registry,
            out Unity.Entities.Hash128 contentHash)
        {
            if (clipSet == null)
            {
                throw new ArgumentNullException(nameof(clipSet));
            }

            ValidateForBakeOrThrow(clipSet);

#if UNITY_EDITOR
            System.Threading.Interlocked.Increment(ref buildInvocationCount);
#endif
            registry = BuildValidatedBlob(clipSet, Allocator.Persistent);
            contentHash = ComposeDedupKey(HashRegistry(registry), clipSet.stableId);
        }

        /// <summary>
        /// Computes the <c>BlobAssetStore</c> dedup key of a clip set without handing the caller a
        /// blob to own, so a baker can probe the store before deciding to build.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what makes the canonical baker pattern expressible:
        /// </para>
        /// <code>
        /// if (ClipRegistryBuilder.TryComputeContentHash(clipSet, out Unity.Entities.Hash128 hash) &amp;&amp;
        ///     !blobAssetStore.TryGet(hash, out BlobAssetReference&lt;ClipRegistryBlob&gt; registry))
        /// {
        ///     ClipRegistryBuilder.Build(clipSet, out registry, out hash);
        ///     blobAssetStore.TryAdd(hash, registry);
        /// }
        /// </code>
        /// <para>
        /// The hash is defined over the finished blob, so this method builds one internally; it
        /// allocates that blob with <see cref="Allocator.Temp"/> and releases it before returning.
        /// <strong>Nothing is left for the caller to own or dispose</strong>, which is why it can be
        /// called on a store hit without leaking. It is not free — it does the same canonicalisation
        /// work <see cref="Build"/> does — but it costs no persistent allocation and no store entry.
        /// </para>
        /// </remarks>
        /// <param name="clipSet">The set whose dedup key is wanted.</param>
        /// <param name="contentHash">
        /// The dedup key on success — byte-identical to the one <see cref="Build"/> produces for the
        /// same asset; <c>default</c> on failure.
        /// </param>
        /// <returns>
        /// True when the key was computed. False when <paramref name="clipSet"/> is null or carries
        /// validation errors: the set is not bakeable, so it has no key. Call <see cref="Build"/> to
        /// obtain the <see cref="ClipValidationException"/> naming the offending rules.
        /// </returns>
        public static bool TryComputeContentHash(
            ClipSetAsset clipSet,
            out Unity.Entities.Hash128 contentHash)
        {
            contentHash = default;
            if (clipSet == null)
            {
                return false;
            }

            List<ValidationMessage> validationMessages =
                ClipValidation.ValidateSet(clipSet, ValidationStage.Bake);
            if (ClipValidation.HasErrors(validationMessages))
            {
                return false;
            }

            BlobAssetReference<ClipRegistryBlob> probeRegistry =
                BuildValidatedBlob(clipSet, Allocator.Temp);
            try
            {
                contentHash = ComposeDedupKey(HashRegistry(probeRegistry), clipSet.stableId);
            }
            finally
            {
                probeRegistry.Dispose();
            }
            return true;
        }

        // -----------------------------------------------------------------------------------
        // Validation gate and blob assembly.
        // -----------------------------------------------------------------------------------

        private static void ValidateForBakeOrThrow(ClipSetAsset clipSet)
        {
            // ValidationStage.Bake names the caller truthfully. It selects the severity of rule V08
            // only, and V08 additionally requires a freshly recomputed VAT source hash to compare
            // against — which this assembly cannot produce, because recomputing it means re-reading
            // the source mesh and AnimationClips through the editor-only VatTextureBaker that
            // architecture section 1.3 forbids Authoring from referencing. V08 is therefore silent
            // at entity bake and is judged in the Editor assembly instead (architecture section 3.5).
            List<ValidationMessage> validationMessages =
                ClipValidation.ValidateSet(clipSet, ValidationStage.Bake);
            if (ClipValidation.HasErrors(validationMessages))
            {
                throw new ClipValidationException(validationMessages);
            }
        }

        private static BlobAssetReference<ClipRegistryBlob> BuildValidatedBlob(
            ClipSetAsset clipSet,
            AllocatorManager.AllocatorHandle allocator)
        {
            // Validation guarantees a rig with 1..8 layers, unique target ids, unique clip ids, and
            // every track bound to a target the rig actually declares.
            RigAsset rig = clipSet.rig;
            List<RigTargetDefinition> canonicalTargets = BuildCanonicalTargets(rig);
            Dictionary<uint, int> denseTargetIndexById = BuildDenseTargetIndexMap(canonicalTargets);
            List<ClipAsset> canonicalClips = BuildCanonicalClips(clipSet);

            return BuildBlob(
                clipSet, rig, canonicalTargets, denseTargetIndexById, canonicalClips, allocator);
        }

        // -----------------------------------------------------------------------------------
        // Canonical ordering (architecture section 4.5 point 1).
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

        private static List<ClipAsset> BuildCanonicalClips(ClipSetAsset clipSet)
        {
            List<ClipAsset> canonicalClips = new List<ClipAsset>();
            // Dedup by asset identity. UnityEngine.Object overrides Equals/GetHashCode to compare
            // instances, so the set itself carries the identity semantics — no instance-id call.
            HashSet<ClipAsset> seenClips = new HashSet<ClipAsset>();
            if (clipSet.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }
                    // Rule V11: a clip listed twice contributes exactly one baked entry.
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
        // do not need one: rule V05 rejects duplicate target ids inside a rig and duplicate clip ids
        // inside a set, and Build runs that gate first, so the ids being compared are unique and the
        // comparison is already a total order. Without V05 an equal-id pair would be ordered
        // arbitrarily by List<T>.Sort's unstable introsort and the bake would stop being
        // deterministic — the dependency is real, which is why it is written down here.

        private static int CompareTargetsByStableId(RigTargetDefinition left, RigTargetDefinition right)
        {
            return left.stableId.CompareTo(right.stableId);
        }

        private static int CompareClipsByStableId(ClipAsset left, ClipAsset right)
        {
            return left.stableId.CompareTo(right.stableId);
        }

        // -----------------------------------------------------------------------------------
        // Blob construction (architecture section 4.2).
        // -----------------------------------------------------------------------------------

        private static BlobAssetReference<ClipRegistryBlob> BuildBlob(
            ClipSetAsset clipSet,
            RigAsset rig,
            List<RigTargetDefinition> canonicalTargets,
            Dictionary<uint, int> denseTargetIndexById,
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
                registryRoot.setKey = clipSet.stableId;
                registryRoot.vatSetKey = clipSet.vatTextures == null ? 0UL : clipSet.vatTextures.setKey;
                registryRoot.layerCount = (byte)rig.layers.Count;
                registryRoot.vatInfo = BuildVatInfo(clipSet.vatTextures);

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
                        clipSet.vatTextures,
                        denseTargetIndexById,
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
            if (vatTextures == null)
            {
                // No VAT data. ClipRegistryBlob.vatSetKey == 0 is the authoritative "no VAT" signal;
                // rowsPerFrame stays 1 so that any consumer dividing by it cannot trip over a zero.
                return new VatTextureInfoBlob
                {
                    flavor = VatFlavor.BoneMatrix,
                    textureWidth = 0,
                    rowsPerFrame = 1,
                    boneOrVertexCount = 0
                };
            }
            return new VatTextureInfoBlob
            {
                flavor = vatTextures.flavor,
                textureWidth = vatTextures.textureWidth,
                rowsPerFrame = vatTextures.rowsPerFrame,
                boneOrVertexCount = vatTextures.flavor == VatFlavor.BoneMatrix
                    ? vatTextures.boneCount
                    : vatTextures.vertexCount
            };
        }

        /// <summary>
        /// Writes the binary-search key array. <paramref name="canonicalClips"/> is already sorted
        /// ascending by clip id and <see cref="ClipRegistryBlob.clips"/> was filled from it in that
        /// order, so this array is simply those ids in the same positions — which is exactly why
        /// <c>ClipRegistryUtil.TryResolveClip</c> can return the search position as the dense index
        /// (architecture sections 4.2, 4.3).
        /// </summary>
        /// <param name="builder">The blob builder owning the registry under construction.</param>
        /// <param name="registryRoot">The registry root whose id array is being allocated.</param>
        /// <param name="canonicalClips">The clips in dense (ascending-id) order.</param>
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
            VatTextureSetAsset vatTextures,
            Dictionary<uint, int> denseTargetIndexById,
            float3[] targetBoundsExtents)
        {
            clipBlob.clipId = clip.stableId;

            // Truncation is intentional: debugName is a 61-byte log/inspector label, never a key.
            FixedString64Bytes debugName = default;
            debugName.CopyFromTruncated(clip.name);
            clipBlob.debugName = debugName;

            clipBlob.duration = clip.duration;
            // Section 4.2: the baked default is always a resolved mode. UseClipDefault is a
            // command-side sentinel and would be circular here, so it resolves to Once.
            clipBlob.defaultLoop = clip.defaultLoop == LoopMode.UseClipDefault
                ? LoopMode.Once
                : clip.defaultLoop;
            // Rule V12: blend defaults longer than the clip are warned about and clamped here.
            clipBlob.defaultBlendIn = math.clamp(clip.defaultBlendIn, 0f, clip.duration);
            clipBlob.defaultBlendOut = math.clamp(clip.defaultBlendOut, 0f, clip.duration);

            List<TransformTrackEntry> transformTrackEntries =
                BuildTransformTrackEntries(clip, denseTargetIndexById);
            List<SpriteTrackEntry> spriteTrackEntries =
                BuildSpriteTrackEntries(clip, denseTargetIndexById);

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

        private struct EventEntry
        {
            public float normalizedTime;
            public int authoringIndex;
            public EventMarker marker;
        }

        private static List<TransformTrackEntry> BuildTransformTrackEntries(
            ClipAsset clip,
            Dictionary<uint, int> denseTargetIndexById)
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
                entries.Add(new TransformTrackEntry
                {
                    denseTargetIndex = denseTargetIndexById[track.targetId],
                    authoringIndex = authoringIndex,
                    track = track
                });
            }
            entries.Sort(CompareTransformTrackEntries);
            return entries;
        }

        private static List<SpriteTrackEntry> BuildSpriteTrackEntries(
            ClipAsset clip,
            Dictionary<uint, int> denseTargetIndexById)
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
                entries.Add(new SpriteTrackEntry
                {
                    denseTargetIndex = denseTargetIndexById[track.targetId],
                    authoringIndex = authoringIndex,
                    track = track
                });
            }
            entries.Sort(CompareSpriteTrackEntries);
            return entries;
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
                        rotationZ = math.radians(authoredKey.rotationZ),
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
                    floatParam = authoredMarker.floatParam
                };
            }
        }

        // -----------------------------------------------------------------------------------
        // Per-target VAT ranges (C10, multi-source VAT tracks).
        // -----------------------------------------------------------------------------------

        private struct VatTargetRangeEntry
        {
            public int denseTargetIndex;
            public int authoringIndex;
            public VatClipRange range;
        }

        /// <summary>
        /// Fills <see cref="ClipBlob.vatTargetRanges"/> from every exact (clip, target) range
        /// <paramref name="vatTextures"/> holds for <paramref name="clip"/> — i.e. every entry of
        /// <c>VatTextureSetAsset.clipRanges</c> whose <c>clipId</c> matches this clip and whose
        /// <c>targetId</c> is non-zero.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The baked texture set, not <see cref="ClipAsset.vatTracks"/>, is the source of truth
        /// here — exactly as <see cref="FillClip"/> already resolves the untargeted range through
        /// <c>VatTextureSetAsset.TryGetClipRange</c> rather than reading
        /// <c>ClipAsset.vatSource</c> directly. <c>vatTracks</c> is authoring <em>intent</em> (what
        /// the next bake should sample); <c>vatTextures.clipRanges</c> is what was actually baked.
        /// Validation rule V07 (see <c>ClipValidation.HasExactVatTrackRange</c>) already guarantees
        /// that every track with a source clip has a matching exact range by the time
        /// <see cref="Build"/> reaches here, so reading the baked ranges cannot silently drop a
        /// track — it can only ever add coverage validation did not require (there is none, because
        /// a stray range with no authoring track is harmless: nothing resolves it unless some part's
        /// dense target index happens to match, and if it does, that is exactly the range that part
        /// should play).
        /// </para>
        /// <para>
        /// A range naming a target id the current rig does not declare (stale data left over after a
        /// target was removed from the rig) is skipped rather than thrown on: validation would have
        /// already reported this rig/clip combination as broken elsewhere, and a defensive skip here
        /// keeps <see cref="Build"/>'s bounds-checked <c>Dictionary</c> lookup from being the thing
        /// that turns a stale texture set into a hard failure instead of a validation message.
        /// </para>
        /// </remarks>
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
        // Bounds at bake (architecture section 4.6).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Unions, per architecture section 4.6: every transform key's <c>position.xy</c> offset
        /// grown by its target's authored half-extents scaled by
        /// <c>max(|scale.x|, |scale.y|, 1)</c>; the origin-centred rest box of every target no key
        /// moved; and the exact measured bounds of the clip's VAT range when it has one.
        /// Conservative by construction, because all five interpolation modes are monotonic between
        /// keys, so the keys bound the extremes of the sampled curve.
        /// </summary>
        /// <remarks>
        /// The result is in <em>offset space</em>, not actor space — see
        /// <see cref="ClipBlob.offsetBounds"/>. Keys are local offsets from a target's rest pose
        /// (architecture section 3.2) and rest poses live on the actor prefab, which this builder
        /// never sees, so every contributing box is centred on the origin. Combining these with the
        /// rest poses to obtain an actor-space box is entity baking's job.
        /// </remarks>
        /// <param name="transformTrackEntries">The clip's transform tracks in canonical order.</param>
        /// <param name="targetBoundsExtents">Per dense target index, the authored half-extents.</param>
        /// <param name="hasVatRange">True when the clip has a baked VAT frame range.</param>
        /// <param name="bakedRange">The VAT range; ignored unless <paramref name="hasVatRange"/> is true.</param>
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
                    float scaleFactor = math.max(
                        math.max(math.abs(authoredKey.scale.x), math.abs(authoredKey.scale.y)),
                        1f);
                    float3 scaledExtents = targetExtents * scaleFactor;
                    float3 keyCenter = new float3(authoredKey.position.x, authoredKey.position.y, 0f);
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
        // Content hash (architecture section 4.5 point 3).
        // -----------------------------------------------------------------------------------

        private static Unity.Entities.Hash128 ComposeDedupKey(ulong contentHash64, ulong setKey)
        {
            return new Unity.Entities.Hash128(
                (uint)contentHash64,
                (uint)(contentHash64 >> 32),
                (uint)SchemaVersion,
                (uint)setKey ^ (uint)(setKey >> 32));
        }

        private static ulong HashRegistry(BlobAssetReference<ClipRegistryBlob> registry)
        {
            ref ClipRegistryBlob registryRoot = ref registry.Value;
            return ComputeContentHash(ref registryRoot);
        }

        /// <summary>
        /// Hashes the finished blob over the normative canonical stream of architecture
        /// section 4.5, with every float contributed as its <c>math.asuint</c> bit pattern so the
        /// hash is exact rather than rounded.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The stream is, in order: the registry header (<c>schemaVersion</c>, <c>setKey</c>,
        /// <c>vatSetKey</c>, <c>layerCount</c>); the target block (<c>sortedTargetIds.Length</c>,
        /// then per dense target its id and the three components of its half-extents); the four
        /// <see cref="VatTextureInfoBlob"/> fields; the clip count; then every clip in dense
        /// (ascending-id) order as described by <see cref="HashClip"/>. Array lengths are hashed
        /// alongside their elements so that two different array shapes can never produce the same
        /// stream.
        /// </para>
        /// <para>
        /// Two properties make this the right thing to key a <c>BlobAssetStore</c> entry with.
        /// First, the <em>finished blob</em> is hashed rather than the authoring graph, so the key
        /// cannot disagree with the bytes it stands for even across the canonicalisation this
        /// builder performs (degrees to radians, clamped blends, resolved loop mode). Second, the
        /// stream visits <strong>every field of the blob</strong>: a change confined to a target id,
        /// a target's extents, any VAT addressing parameter, or a clip's debug name changes the
        /// hash, so no such edit can be answered from the store with a stale blob.
        /// </para>
        /// </remarks>
        /// <param name="registryRoot">The finished registry blob to hash.</param>
        /// <returns>The 64-bit content hash.</returns>
        private static ulong ComputeContentHash(ref ClipRegistryBlob registryRoot)
        {
            xxHash3.StreamingState hashState = new xxHash3.StreamingState(true);
            hashState.Update(registryRoot.schemaVersion);
            hashState.Update(registryRoot.setKey);
            hashState.Update(registryRoot.vatSetKey);
            hashState.Update(registryRoot.layerCount);

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
        /// <param name="hashState">The streaming hash under construction.</param>
        /// <param name="clipBlob">The baked clip to append.</param>
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
                    hashState.Update(math.asuint(keyBlob.rotationZ));
                    hashState.Update(math.asuint(keyBlob.scale.x));
                    hashState.Update(math.asuint(keyBlob.scale.y));
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

                // sliceSpace was never in this stream, which meant two clips differing only in
                // whether their keys were absolute or rest-relative hashed identically — so flipping
                // it left every consumer's baked registry looking current. Fixed here rather than
                // left alone because this schema bump is the one moment the change costs nothing.
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

            hashState.Update(clipBlob.events.Length);
            for (int eventIndex = 0; eventIndex < clipBlob.events.Length; eventIndex++)
            {
                ref EventMarkerBlob eventBlob = ref clipBlob.events[eventIndex];
                hashState.Update(math.asuint(eventBlob.normalizedTime));
                hashState.Update(eventBlob.eventKey);
                hashState.Update(eventBlob.intParam);
                hashState.Update(math.asuint(eventBlob.floatParam));
            }

            hashState.Update(clipBlob.vatFrameStart);
            hashState.Update(clipBlob.vatFrameCount);
            hashState.Update(math.asuint(clipBlob.vatFps));

            // C10: per-target VAT range overrides join the canonical stream the same way every other
            // array does — length first, then each element — so two blobs that differ only in which
            // parts have a dedicated baked range can never hash the same.
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
