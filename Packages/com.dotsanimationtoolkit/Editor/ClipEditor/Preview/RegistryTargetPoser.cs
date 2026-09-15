// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;

namespace DotsAnimationToolkit.Editor
{
    public enum RegistryBuildOutcome : byte
    {
        Built = 0,
        NoRig = 1,
        NoClipSets = 2,
        ValidationErrors = 3,
        Failed = 4
    }

    /// <summary>
    /// Owns one preview-scoped registry blob built from a rig plus clip sets, and poses every dense
    /// target of one clip through ClipSampler into a host's writer. Each host words its own status.
    /// </summary>
    public sealed class RegistryTargetPoser : IDisposable
    {
        public interface ITargetPoseWriter
        {
            // False skips the target: the host has nothing to write it onto.
            bool TryGetRestPose(uint targetId, out TargetRestPose restPose);

            void WritePose(uint targetId, in TargetPose pose);
        }

        private BlobAssetReference<ClipRegistryBlob> registry;

        public BlobAssetReference<ClipRegistryBlob> Registry
        {
            get { return registry; }
        }

        public bool HasRegistry
        {
            get { return registry.IsCreated; }
        }

        public RegistryBuildOutcome LastOutcome { get; private set; } = RegistryBuildOutcome.NoRig;

        // The exception message when LastOutcome is Failed; empty otherwise.
        public string FailureMessage { get; private set; } = string.Empty;

        public RegistryBuildOutcome Rebuild(RigAsset rig, IReadOnlyList<ClipSetAsset> clipSets)
        {
            Release();
            FailureMessage = string.Empty;

            if (rig == null)
            {
                LastOutcome = RegistryBuildOutcome.NoRig;
                return LastOutcome;
            }
            if (clipSets == null || clipSets.Count == 0)
            {
                LastOutcome = RegistryBuildOutcome.NoClipSets;
                return LastOutcome;
            }

            try
            {
                Unity.Entities.Hash128 contentHash;
                ClipRegistryBuilder.Build(rig, clipSets, out registry, out contentHash);
                LastOutcome = RegistryBuildOutcome.Built;
            }
            catch (ArgumentNullException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                LastOutcome = RegistryBuildOutcome.NoRig;
            }
            catch (ClipValidationException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                LastOutcome = RegistryBuildOutcome.ValidationErrors;
            }
            catch (Exception buildException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                FailureMessage = buildException.Message;
                LastOutcome = RegistryBuildOutcome.Failed;
            }

            return LastOutcome;
        }

        public bool TryResolveClipIndex(ulong clipId, out int clipIndex)
        {
            clipIndex = -1;
            if (!registry.IsCreated)
            {
                return false;
            }

            ref ClipRegistryBlob registryBlob = ref registry.Value;
            for (int index = 0; index < registryBlob.sortedClipIds.Length; index++)
            {
                if (registryBlob.sortedClipIds[index] == clipId)
                {
                    clipIndex = index;
                    return true;
                }
            }
            return false;
        }

        public bool IsClipInRegistry(ulong clipId)
        {
            int clipIndex;
            return TryResolveClipIndex(clipId, out clipIndex);
        }

        // False when no registry is built or the clip is not in it.
        public bool PoseTargets(ulong clipId, float normalizedTime, ITargetPoseWriter writer)
        {
            if (!registry.IsCreated || writer == null)
            {
                return false;
            }

            int clipIndex;
            if (!TryResolveClipIndex(clipId, out clipIndex))
            {
                return false;
            }

            ref ClipRegistryBlob registryBlob = ref registry.Value;
            ref ClipBlob clipBlob = ref registryBlob.clips[clipIndex];

            for (int targetIndex = 0; targetIndex < registryBlob.sortedTargetIds.Length; targetIndex++)
            {
                uint targetId = registryBlob.sortedTargetIds[targetIndex];

                TargetRestPose restPose;
                if (!writer.TryGetRestPose(targetId, out restPose))
                {
                    continue;
                }

                TargetPose pose;
                ClipSampler.SamplePose(ref clipBlob, targetIndex, normalizedTime, in restPose, out pose);
                writer.WritePose(targetId, in pose);
            }

            return true;
        }

        public void Release()
        {
            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            registry = default(BlobAssetReference<ClipRegistryBlob>);
        }

        public void Dispose()
        {
            Release();
        }
    }
}
