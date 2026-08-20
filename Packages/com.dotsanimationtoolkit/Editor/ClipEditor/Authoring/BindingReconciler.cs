// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>What kind of binding a <see cref="BrokenBinding"/> describes.</summary>
    public enum BrokenBindingKind
    {
        /// <summary>A bone track whose <c>boneName</c> matches nothing in the prefab.</summary>
        BoneTrack,

        /// <summary>A bone-mode socket whose <c>boneName</c> matches nothing in the prefab.</summary>
        BoneSocket,

        /// <summary>A rig target whose <c>displayName</c> matches no prefab transform.</summary>
        RigTargetRestPose
    }

    /// <summary>One binding that no longer resolves against the prefab, and where it came from.</summary>
    public sealed class BrokenBinding
    {
        public BrokenBindingKind kind;

        /// <summary>The name that failed to resolve.</summary>
        public string missingName = string.Empty;

        /// <summary>Human-readable source, for the panel's row label.</summary>
        public string description = string.Empty;

        /// <summary>Index into the owning list — the clip's boneTracks, the rig's sockets/targets.</summary>
        public int index = -1;

        /// <summary>The clip the binding belongs to, or null for a rig-scoped binding.</summary>
        public ClipAsset clip;

        /// <summary>How many keys would be lost by deleting this track. Zero for non-track kinds.</summary>
        public int keyCount;
    }

    /// <summary>
    /// Finds animation bindings that a prefab restructure has broken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Only name-based bindings can break, and knowing which those are is the whole
    /// point.</strong> Transform and sprite tracks bind to a rig target's <em>stable id</em>, minted
    /// once and never derived from a name, so renaming a part or reparenting it changes nothing they
    /// depend on — that is precisely why the ids exist. Reporting them as "possibly broken" after
    /// every prefab edit would be noise that trains the user to dismiss the panel unread.
    /// </para>
    /// <para>
    /// Three bindings are genuinely name-based and do break:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>BoneTrack.boneName</c> — the bake resolves it by name against the skinned prefab. A rename
    /// leaves the track authored but posing nothing, which shows up as an animation that silently
    /// does not play.
    /// </description></item>
    /// <item><description>
    /// A socket with <c>mode = Bone</c>, for the same reason. The bake reports these, but after the
    /// bake, which is late.
    /// </description></item>
    /// <item><description>
    /// A rig target's <c>displayName</c>, which is how the Clip Editor's preview finds the prefab
    /// transform to take a rest pose from. Breaking this loses no authored data — the track still
    /// plays — but the part drops back to the origin at unit scale in the preview, which looks like
    /// a corrupted clip.
    /// </description></item>
    /// </list>
    /// <para>
    /// The severity difference matters and is carried through to the panel: the first two are data
    /// that will not bake, the third is a preview that will mislead.
    /// </para>
    /// </remarks>
    public static class BindingReconciler
    {
        /// <summary>
        /// Collects every binding in <paramref name="clipSet"/> that does not resolve against
        /// <paramref name="availableNames"/>.
        /// </summary>
        /// <param name="clipSet">The set whose clips and rig are checked.</param>
        /// <param name="availableNames">
        /// Every transform name present in the prefab. An empty set means no prefab is loaded, in
        /// which case nothing is reported — "you have not assigned a rig" is not a broken binding.
        /// </param>
        /// <param name="findings">Cleared and filled with the broken bindings, in discovery order.</param>
        public static void Collect(
            ClipSetAsset clipSet, HashSet<string> availableNames, List<BrokenBinding> findings)
        {
            findings.Clear();
            if (clipSet == null || availableNames == null || availableNames.Count == 0)
            {
                return;
            }

            CollectBoneTracks(clipSet, availableNames, findings);
            CollectRigBindings(clipSet.rig, availableNames, findings);
        }

        private static void CollectBoneTracks(
            ClipSetAsset clipSet, HashSet<string> availableNames, List<BrokenBinding> findings)
        {
            if (clipSet.clips == null)
            {
                return;
            }

            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null || clip.boneTracks == null)
                {
                    continue;
                }

                for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
                {
                    BoneTrack track = clip.boneTracks[trackIndex];
                    if (track == null || string.IsNullOrEmpty(track.boneName)
                        || availableNames.Contains(track.boneName))
                    {
                        continue;
                    }

                    findings.Add(new BrokenBinding
                    {
                        kind = BrokenBindingKind.BoneTrack,
                        missingName = track.boneName,
                        description = clip.name + "  ·  bone track",
                        index = trackIndex,
                        clip = clip,
                        keyCount = track.keys != null ? track.keys.Count : 0
                    });
                }
            }
        }

        private static void CollectRigBindings(
            RigAsset rig, HashSet<string> availableNames, List<BrokenBinding> findings)
        {
            if (rig == null)
            {
                return;
            }

            if (rig.sockets != null)
            {
                for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
                {
                    SocketDefinition socket = rig.sockets[socketIndex];
                    if (socket == null || socket.mode != SocketAttachMode.Bone
                        || string.IsNullOrEmpty(socket.boneName)
                        || availableNames.Contains(socket.boneName))
                    {
                        continue;
                    }

                    findings.Add(new BrokenBinding
                    {
                        kind = BrokenBindingKind.BoneSocket,
                        missingName = socket.boneName,
                        description = "Rig  ·  socket "
                            + (string.IsNullOrEmpty(socket.displayName)
                                ? socket.Id.Value.ToString()
                                : socket.displayName),
                        index = socketIndex
                    });
                }
            }

            if (rig.targets == null)
            {
                return;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.displayName)
                    || availableNames.Contains(target.displayName))
                {
                    continue;
                }

                findings.Add(new BrokenBinding
                {
                    kind = BrokenBindingKind.RigTargetRestPose,
                    missingName = target.displayName,
                    description = "Rig  ·  target rest pose",
                    index = targetIndex
                });
            }
        }

        /// <summary>
        /// Points a broken binding at <paramref name="newName"/>.
        /// </summary>
        /// <remarks>
        /// The caller records undo and marks dirty; this only performs the write, so the same
        /// function is usable from a test with no editor state involved.
        /// </remarks>
        /// <returns>False when the binding's index no longer addresses anything.</returns>
        public static bool Remap(BrokenBinding binding, RigAsset rig, string newName)
        {
            if (binding == null || string.IsNullOrEmpty(newName))
            {
                return false;
            }

            switch (binding.kind)
            {
                case BrokenBindingKind.BoneTrack:
                    if (binding.clip == null || binding.clip.boneTracks == null
                        || binding.index < 0 || binding.index >= binding.clip.boneTracks.Count)
                    {
                        return false;
                    }
                    binding.clip.boneTracks[binding.index].boneName = newName;
                    return true;

                case BrokenBindingKind.BoneSocket:
                    if (rig == null || rig.sockets == null
                        || binding.index < 0 || binding.index >= rig.sockets.Count)
                    {
                        return false;
                    }
                    rig.sockets[binding.index].boneName = newName;
                    return true;

                case BrokenBindingKind.RigTargetRestPose:
                    if (rig == null || rig.targets == null
                        || binding.index < 0 || binding.index >= rig.targets.Count)
                    {
                        return false;
                    }
                    // Renaming the target is safe: tracks bind to its stable id, which this does not
                    // touch. That is the whole reason the id is not derived from the name.
                    rig.targets[binding.index].displayName = newName;
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Removes what a broken binding points at.
        /// </summary>
        /// <remarks>
        /// Only offered for the two track-like kinds. A rig target is not deletable from here: it
        /// carries the stable id that every transform and sprite track in every clip of the set
        /// binds to, so removing it to fix a preview annoyance would break animation data that was
        /// working perfectly. Renaming it is the fix; deleting it is a rig edit, made in the rig
        /// asset where its consequences are visible.
        /// </remarks>
        /// <returns>False when the binding is not deletable or no longer addresses anything.</returns>
        public static bool Delete(BrokenBinding binding, RigAsset rig)
        {
            if (binding == null)
            {
                return false;
            }

            switch (binding.kind)
            {
                case BrokenBindingKind.BoneTrack:
                    if (binding.clip == null || binding.clip.boneTracks == null
                        || binding.index < 0 || binding.index >= binding.clip.boneTracks.Count)
                    {
                        return false;
                    }
                    binding.clip.boneTracks.RemoveAt(binding.index);
                    return true;

                case BrokenBindingKind.BoneSocket:
                    if (rig == null || rig.sockets == null
                        || binding.index < 0 || binding.index >= rig.sockets.Count)
                    {
                        return false;
                    }
                    rig.sockets.RemoveAt(binding.index);
                    return true;
            }
            return false;
        }

        /// <summary>Whether <see cref="Delete"/> will do anything for this kind.</summary>
        public static bool IsDeletable(BrokenBindingKind kind)
        {
            return kind == BrokenBindingKind.BoneTrack || kind == BrokenBindingKind.BoneSocket;
        }
    }
}
