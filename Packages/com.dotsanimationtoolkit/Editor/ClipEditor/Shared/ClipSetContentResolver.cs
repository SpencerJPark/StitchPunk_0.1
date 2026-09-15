using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reports which kinds of content a bound set of clip sets carries (bone/VAT versus
    /// non-VAT tracks), for preview status lines and toggle enablement.
    /// </summary>
    public static class ClipSetContentResolver
    {
        public static bool HasBoneOrVatContent(IReadOnlyList<ClipSetAsset> clipSets)
        {
            if (clipSets == null)
            {
                return false;
            }

            for (int clipSetIndex = 0; clipSetIndex < clipSets.Count; clipSetIndex++)
            {
                ClipSetAsset clipSetAsset = clipSets[clipSetIndex];
                if (clipSetAsset == null)
                {
                    continue;
                }

                if (clipSetAsset.vatTextures != null)
                {
                    return true;
                }

                List<ClipAsset> clips = clipSetAsset.clips;
                if (clips == null)
                {
                    continue;
                }

                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    ClipAsset clipAsset = clips[clipIndex];
                    if (clipAsset == null)
                    {
                        continue;
                    }

                    if (clipAsset.boneTracks != null && clipAsset.boneTracks.Count > 0)
                    {
                        return true;
                    }

                    // Unity deserializes a serializable class field as a non-null instance, so an empty source is only recognisable by its clip.
                    if (clipAsset.vatSource != null && clipAsset.vatSource.sourceClip != null)
                    {
                        return true;
                    }

                    if (clipAsset.vatTracks != null && clipAsset.vatTracks.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool HasNonVatContent(IReadOnlyList<ClipSetAsset> clipSets)
        {
            if (clipSets == null)
            {
                return false;
            }

            for (int clipSetIndex = 0; clipSetIndex < clipSets.Count; clipSetIndex++)
            {
                ClipSetAsset clipSetAsset = clipSets[clipSetIndex];
                if (clipSetAsset == null)
                {
                    continue;
                }

                List<ClipAsset> clips = clipSetAsset.clips;
                if (clips == null)
                {
                    continue;
                }

                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    ClipAsset clipAsset = clips[clipIndex];
                    if (clipAsset == null)
                    {
                        continue;
                    }

                    if (clipAsset.transformTracks != null && clipAsset.transformTracks.Count > 0)
                    {
                        return true;
                    }

                    if (clipAsset.spriteTracks != null && clipAsset.spriteTracks.Count > 0)
                    {
                        return true;
                    }

                    if (clipAsset.billboardTracks != null && clipAsset.billboardTracks.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
