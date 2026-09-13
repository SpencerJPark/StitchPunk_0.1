// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Decides whether one clip track's binding lands on a given rig target. A non-zero tag on the
    /// track always wins over its raw target id, the same sentinel convention the tracks use.
    /// </summary>
    public static class TrackTargetMatchResolver
    {
        public static bool TrackBindsTarget(uint trackTargetId, uint trackTagId, RigTargetDefinition target)
        {
            if (target == null)
            {
                return false;
            }

            return TrackBindsTarget(trackTargetId, trackTagId, target.Id.Value, target.tagId);
        }

        /// <param name="targetTagId">0 for an untagged target, which only a raw-id track can bind.</param>
        public static bool TrackBindsTarget(uint trackTargetId, uint trackTagId, uint targetStableId, uint targetTagId)
        {
            if (trackTagId != 0)
            {
                return targetTagId == trackTagId;
            }

            return trackTargetId == targetStableId;
        }
    }
}
