// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Counts how many rig targets bind a given <see cref="TargetTagEntry"/>'s id (Phase E
    /// target-tags spec §4.2.2): the number <see cref="TargetTagRegistryEditor"/> shows before a
    /// delete, so the cost of removing a tag is visible before it happens rather than discovered
    /// afterwards in the console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Split into a pure overload and a project-scanning one</strong>, mirroring
    /// <see cref="RigAssetUtility"/>'s separation of asset creation from the write path around it.
    /// <see cref="CountRigTargetBindings(uint, IReadOnlyList{RigAsset})"/> does the actual counting
    /// against whatever rig list it is handed and touches no asset database — it is what an EditMode
    /// fixture calls directly, against in-memory rigs, with no disk I/O in the way.
    /// <see cref="CountRigTargetBindings(TargetTagEntry)"/> is the thin project-wide entry point the
    /// registry editor actually uses, and does nothing but find every <see cref="RigAsset"/> in the
    /// project and hand the list to the pure overload.
    /// </para>
    /// <para>
    /// <strong>E3 made the TODO real.</strong> <see cref="TransformTrack"/> and <see cref="SpriteTrack"/>
    /// can now bind a tag directly (spec §4.3), so a delete's real cost is rig-target bindings
    /// (<see cref="CountRigTargetBindings(TargetTagEntry)"/>) plus track bindings
    /// (<see cref="CountTrackBindings(TargetTagEntry)"/>) — <see cref="TargetTagRegistryEditor"/>
    /// sums both before it lets a delete through.
    /// </para>
    /// </remarks>
    public static class TargetTagBindingUtility
    {
        /// <summary>
        /// Counts, across <paramref name="rigs"/>, the rig target rows whose <c>tagId</c> equals
        /// <paramref name="tagId"/>. Pure — no asset database access, so it is exactly as fast and
        /// as testable as the loop it is.
        /// </summary>
        /// <param name="tagId">The tag id to count bindings for. 0 ("untagged") always counts 0,
        /// since untagged is not a binding to anything (spec §4.2) — it would otherwise report a
        /// number proportional to however many ordinary, untagged targets a rig happens to have.</param>
        /// <param name="rigs">The rigs to search. Null, or a null entry within it, contributes 0.</param>
        /// <returns>The total number of matching target rows.</returns>
        public static int CountRigTargetBindings(uint tagId, IReadOnlyList<RigAsset> rigs)
        {
            if (tagId == 0u || rigs == null)
            {
                return 0;
            }

            int bindingCount = 0;
            for (int rigIndex = 0; rigIndex < rigs.Count; rigIndex++)
            {
                RigAsset rig = rigs[rigIndex];
                if (rig == null || rig.targets == null)
                {
                    continue;
                }
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                    if (targetDefinition != null && targetDefinition.tagId == tagId)
                    {
                        bindingCount++;
                    }
                }
            }
            return bindingCount;
        }

        /// <summary>
        /// Counts <paramref name="entry"/>'s rig target bindings across every <see cref="RigAsset"/>
        /// the project's asset database can find. The entry point
        /// <see cref="TargetTagRegistryEditor"/> actually calls.
        /// </summary>
        /// <param name="entry">The tag entry about to be removed. Null, or an entry still at the
        /// reserved 0 id, counts 0.</param>
        /// <returns>The number of matching target rows project-wide.</returns>
        public static int CountRigTargetBindings(TargetTagEntry entry)
        {
            if (entry == null || entry.stableId == 0u)
            {
                return 0;
            }
            return CountRigTargetBindings(entry.stableId, FindAllRigAssetsInProject());
        }

        private static List<RigAsset> FindAllRigAssetsInProject()
        {
            List<RigAsset> rigs = new List<RigAsset>();
            string[] rigAssetGuids = AssetDatabase.FindAssets("t:" + nameof(RigAsset));
            for (int guidIndex = 0; guidIndex < rigAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(rigAssetGuids[guidIndex]);
                RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(assetPath);
                if (rig != null)
                {
                    rigs.Add(rig);
                }
            }
            return rigs;
        }

        /// <summary>
        /// Counts, across <paramref name="clips"/>, the <see cref="TransformTrack"/> and
        /// <see cref="SpriteTrack"/> rows whose <c>tagId</c> equals <paramref name="tagId"/> (Phase E
        /// target-tags spec §4.3, E3). Pure — no asset database access — mirroring
        /// <see cref="CountRigTargetBindings(uint, IReadOnlyList{RigAsset})"/>'s split.
        /// </summary>
        /// <param name="tagId">The tag id to count bindings for. 0 always counts 0, since 0 means
        /// "bind by target id instead" (the track's sentinel convention), never a binding to
        /// anything.</param>
        /// <param name="clips">The clips to search. Null, or a null entry within it, contributes 0.</param>
        /// <returns>The total number of matching track rows.</returns>
        public static int CountTrackBindings(uint tagId, IReadOnlyList<ClipAsset> clips)
        {
            if (tagId == 0u || clips == null)
            {
                return 0;
            }

            int bindingCount = 0;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                if (clip.transformTracks != null)
                {
                    for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                    {
                        TransformTrack track = clip.transformTracks[trackIndex];
                        if (track != null && track.tagId == tagId)
                        {
                            bindingCount++;
                        }
                    }
                }

                if (clip.spriteTracks != null)
                {
                    for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                    {
                        SpriteTrack track = clip.spriteTracks[trackIndex];
                        if (track != null && track.tagId == tagId)
                        {
                            bindingCount++;
                        }
                    }
                }
            }
            return bindingCount;
        }

        /// <summary>
        /// Counts <paramref name="entry"/>'s track bindings across every <see cref="ClipAsset"/> the
        /// project's asset database can find, main asset or sub-asset of a
        /// <see cref="ClipSetAsset"/> alike (a clip may be either — see <see cref="ClipAsset"/>'s own
        /// remarks). The entry point <see cref="TargetTagRegistryEditor"/> sums alongside
        /// <see cref="CountRigTargetBindings(TargetTagEntry)"/> before a delete.
        /// </summary>
        /// <param name="entry">The tag entry about to be removed. Null, or an entry still at the
        /// reserved 0 id, counts 0.</param>
        /// <returns>The number of matching track rows project-wide.</returns>
        public static int CountTrackBindings(TargetTagEntry entry)
        {
            if (entry == null || entry.stableId == 0u)
            {
                return 0;
            }
            return CountTrackBindings(entry.stableId, FindAllClipAssetsInProject());
        }

        /// <summary>
        /// Finds every <see cref="ClipAsset"/> in the project, whether it is a free-standing asset or
        /// a sub-asset of a <see cref="ClipSetAsset"/> file.
        /// </summary>
        /// <remarks>
        /// <see cref="AssetDatabase.FindAssets(string)"/> indexes sub-assets by type but returns only
        /// their containing file's GUID, and it can return that same GUID once per matching
        /// sub-asset — so this dedups by path and then reads every object at that path with
        /// <see cref="AssetDatabase.LoadAllAssetsAtPath"/> rather than
        /// <c>LoadAssetAtPath&lt;ClipAsset&gt;</c>, which would silently miss a clip that is not a
        /// file's main object.
        /// </remarks>
        private static List<ClipAsset> FindAllClipAssetsInProject()
        {
            List<ClipAsset> clips = new List<ClipAsset>();
            string[] clipAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipAsset));
            HashSet<string> seenPaths = new HashSet<string>();
            for (int guidIndex = 0; guidIndex < clipAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipAssetGuids[guidIndex]);
                if (string.IsNullOrEmpty(assetPath) || !seenPaths.Add(assetPath))
                {
                    continue;
                }
                UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0; assetIndex < assetsAtPath.Length; assetIndex++)
                {
                    ClipAsset clip = assetsAtPath[assetIndex] as ClipAsset;
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }
            return clips;
        }
    }
}
