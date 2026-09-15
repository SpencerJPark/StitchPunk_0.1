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
            return LastOutcome;
        }

        public bool TryResolveClipIndex(ulong clipId, out int clipIndex)
        {
            clipIndex = -1;
            return false;
        }

        public bool IsClipInRegistry(ulong clipId)
        {
            return false;
        }

        // False when no registry is built or the clip is not in it.
        public bool PoseTargets(ulong clipId, float normalizedTime, ITargetPoseWriter writer)
        {
            return false;
        }

        public void Release()
        {
        }

        public void Dispose()
        {
        }
    }
}
