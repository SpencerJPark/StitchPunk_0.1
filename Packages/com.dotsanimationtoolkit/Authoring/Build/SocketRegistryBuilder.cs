// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Builds a <see cref="SocketRegistryBlob"/> from a rig's socket rows and the motion the VAT
    /// bake captured for its bone sockets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately separate from <see cref="ClipRegistryBuilder"/>: sockets are an opt-in
    /// attachment concern, and folding them into the clip registry would change that blob's layout
    /// — bumping its schema version and invalidating its golden content hash — for every project,
    /// including the ones that never attach anything.
    /// </para>
    /// <para>
    /// <strong>Dense indices must agree with the clip registry's.</strong> A rig-target socket
    /// stores a dense target index, and that index is only meaningful if it means the same row the
    /// clip registry means. Both derive it the same way — position in the rig's target ids sorted
    /// ascending — so the two stay in step by using one rule rather than by being checked against
    /// each other.
    /// </para>
    /// </remarks>
    public static class SocketRegistryBuilder
    {
        /// <summary>Blob layout version; bump on any layout change.</summary>
        public const int SchemaVersion = 1;

        /// <summary>True when <paramref name="rig"/> declares at least one usable socket.</summary>
        public static bool HasSockets(RigAsset rig)
        {
            if (rig == null || rig.sockets == null)
            {
                return false;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.IsValid)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Builds the socket blob for <paramref name="rig"/>.
        /// </summary>
        /// <param name="rig">The rig whose sockets are baked.</param>
        /// <param name="vatTextures">
        /// The texture set holding baked bone-socket motion, or null. Null is legitimate — a rig
        /// whose sockets are all rig-target sockets needs no baked motion at all.
        /// </param>
        /// <param name="registry">The built blob; <c>default</c> when the rig has no sockets.</param>
        /// <returns>False when there was nothing to build.</returns>
        public static bool TryBuild(
            RigAsset rig,
            VatTextureSetAsset vatTextures,
            out BlobAssetReference<SocketRegistryBlob> registry)
        {
            registry = default(BlobAssetReference<SocketRegistryBlob>);
            if (!HasSockets(rig))
            {
                return false;
            }

            List<uint> sortedTargetIds = BuildSortedTargetIds(rig);

            // Sorted by id so the runtime can binary-search, and so the same rig always produces
            // byte-identical output whatever order the rows were authored in.
            List<SocketDefinition> orderedSockets = new List<SocketDefinition>();
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.IsValid)
                {
                    orderedSockets.Add(socket);
                }
            }
            orderedSockets.Sort(CompareSocketsById);

            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref SocketRegistryBlob root = ref blobBuilder.ConstructRoot<SocketRegistryBlob>();
                root.schemaVersion = SchemaVersion;
                root.rigKey = rig.StableId;

                BlobBuilderArray<uint> idArray =
                    blobBuilder.Allocate(ref root.sortedSocketIds, orderedSockets.Count);
                BlobBuilderArray<SocketDefinitionBlob> definitionArray =
                    blobBuilder.Allocate(ref root.sockets, orderedSockets.Count);

                for (int socketIndex = 0; socketIndex < orderedSockets.Count; socketIndex++)
                {
                    SocketDefinition socket = orderedSockets[socketIndex];
                    idArray[socketIndex] = socket.Id.Value;

                    int targetIndex = -1;
                    if (socket.mode == SocketAttachMode.RigTarget)
                    {
                        targetIndex = sortedTargetIds.IndexOf(socket.targetId);
                    }

                    Vector3 euler = socket.localEulerAngles;
                    definitionArray[socketIndex] = new SocketDefinitionBlob
                    {
                        mode = socket.mode,
                        targetIndex = targetIndex,
                        layerIndex = (byte)math.clamp(socket.layerIndex, 0, byte.MaxValue),
                        localPosition = new float3(
                            socket.localPosition.x, socket.localPosition.y, socket.localPosition.z),
                        localRotation = quaternion.Euler(math.radians(new float3(euler.x, euler.y, euler.z)))
                    };
                }

                AllocateClipTracks(blobBuilder, ref root, orderedSockets, vatTextures);

                registry = blobBuilder.CreateBlobAssetReference<SocketRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                blobBuilder.Dispose();
            }
            return true;
        }

        /// <summary>
        /// Writes the baked bone-socket motion, skipping anything that does not line up.
        /// </summary>
        /// <remarks>
        /// A track whose positions and rotations differ in length is dropped rather than truncated.
        /// Mismatched lengths mean the bake wrote something it did not intend to, and truncating
        /// would turn a loud bug into a socket that quietly drifts out of sync near the end of a
        /// clip — far harder to trace back here.
        /// </remarks>
        private static void AllocateClipTracks(
            BlobBuilder blobBuilder,
            ref SocketRegistryBlob root,
            List<SocketDefinition> orderedSockets,
            VatTextureSetAsset vatTextures)
        {
            List<VatSocketTrack> usableTracks = new List<VatSocketTrack>();
            List<int> usableSocketIndices = new List<int>();

            if (vatTextures != null && vatTextures.socketTracks != null)
            {
                for (int trackIndex = 0; trackIndex < vatTextures.socketTracks.Count; trackIndex++)
                {
                    VatSocketTrack track = vatTextures.socketTracks[trackIndex];
                    if (track == null || track.positions == null || track.rotations == null)
                    {
                        continue;
                    }
                    if (track.positions.Count == 0 || track.positions.Count != track.rotations.Count)
                    {
                        continue;
                    }

                    int socketIndex = IndexOfSocketId(orderedSockets, track.socketId);
                    if (socketIndex < 0)
                    {
                        // The socket row was deleted since the bake. Dropping the orphan is right:
                        // nothing can reference it, and keeping it would only bloat the blob.
                        continue;
                    }
                    usableTracks.Add(track);
                    usableSocketIndices.Add(socketIndex);
                }
            }

            BlobBuilderArray<SocketClipTrackBlob> trackArray =
                blobBuilder.Allocate(ref root.clipTracks, usableTracks.Count);

            for (int trackIndex = 0; trackIndex < usableTracks.Count; trackIndex++)
            {
                VatSocketTrack track = usableTracks[trackIndex];
                ref SocketClipTrackBlob trackBlob = ref trackArray[trackIndex];
                trackBlob.clipId = track.clipId;
                trackBlob.socketIndex = usableSocketIndices[trackIndex];
                trackBlob.fps = track.fps > 0f ? track.fps : 30f;

                BlobBuilderArray<SocketSampleBlob> sampleArray =
                    blobBuilder.Allocate(ref trackBlob.samples, track.positions.Count);
                for (int sampleIndex = 0; sampleIndex < track.positions.Count; sampleIndex++)
                {
                    Vector3 position = track.positions[sampleIndex];
                    Quaternion rotation = track.rotations[sampleIndex];
                    sampleArray[sampleIndex] = new SocketSampleBlob
                    {
                        position = new float3(position.x, position.y, position.z),
                        rotation = new quaternion(rotation.x, rotation.y, rotation.z, rotation.w)
                    };
                }
            }
        }

        private static List<uint> BuildSortedTargetIds(RigAsset rig)
        {
            List<uint> targetIds = new List<uint>();
            if (rig.targets == null)
            {
                return targetIds;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null && target.Id.IsValid)
                {
                    targetIds.Add(target.Id.Value);
                }
            }
            targetIds.Sort();
            return targetIds;
        }

        private static int IndexOfSocketId(List<SocketDefinition> orderedSockets, uint socketId)
        {
            for (int socketIndex = 0; socketIndex < orderedSockets.Count; socketIndex++)
            {
                if (orderedSockets[socketIndex].Id.Value == socketId)
                {
                    return socketIndex;
                }
            }
            return -1;
        }

        private static int CompareSocketsById(SocketDefinition first, SocketDefinition second)
        {
            return first.Id.Value.CompareTo(second.Id.Value);
        }
    }
}
