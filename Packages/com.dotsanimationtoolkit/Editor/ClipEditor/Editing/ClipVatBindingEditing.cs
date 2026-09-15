// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes a clip's VAT source bindings: target id 0 addresses
    /// <see cref="ClipAsset.vatSource"/>, any other id addresses the matching row in
    /// <see cref="ClipAsset.vatTracks"/>.
    /// </summary>
    public static class ClipVatBindingEditing
    {
        public static AnimationClip GetSourceClip(ClipAsset clip, uint targetId)
        {
            if (clip == null)
            {
                return null;
            }
            if (targetId == 0)
            {
                return clip.vatSource != null ? clip.vatSource.sourceClip : null;
            }
            VatTrack vatTrack = FindVatTrack(clip, targetId);
            return vatTrack != null ? vatTrack.sourceClip : null;
        }

        public static void SetSourceClip(ClipAsset clip, uint targetId, AnimationClip sourceClip)
        {
            if (clip == null)
            {
                return;
            }
            if (targetId == 0)
            {
                SetVatSourceClip(clip, sourceClip);
                return;
            }
            SetVatTrackSourceClip(clip, targetId, sourceClip);
        }

        public static bool GetLoopSafe(ClipAsset clip, uint targetId)
        {
            if (clip == null)
            {
                return false;
            }
            if (targetId == 0)
            {
                return clip.vatSource != null && clip.vatSource.loopSafe;
            }
            VatTrack vatTrack = FindVatTrack(clip, targetId);
            return vatTrack != null && vatTrack.loopSafe;
        }

        public static void SetLoopSafe(ClipAsset clip, uint targetId, bool loopSafe)
        {
            if (clip == null)
            {
                return;
            }
            if (targetId == 0)
            {
                if (clip.vatSource == null || clip.vatSource.loopSafe == loopSafe)
                {
                    return;
                }
                Undo.RecordObject(clip, "Set VAT loop safe");
                clip.vatSource.loopSafe = loopSafe;
                EditorUtility.SetDirty(clip);
                return;
            }
            VatTrack vatTrack = FindVatTrack(clip, targetId);
            if (vatTrack == null || vatTrack.loopSafe == loopSafe)
            {
                return;
            }
            Undo.RecordObject(clip, "Set VAT loop safe");
            vatTrack.loopSafe = loopSafe;
            EditorUtility.SetDirty(clip);
        }

        public static string DescribeLengthMismatch(ClipAsset clip, AnimationClip sourceClip)
        {
            if (clip == null || sourceClip == null)
            {
                return string.Empty;
            }
            float halfFrame = 0.5f / Mathf.Max(1f, clip.frameRate);
            float difference = sourceClip.length - clip.duration;
            if (Mathf.Abs(difference) <= halfFrame)
            {
                return string.Empty;
            }
            if (difference > 0f)
            {
                return "the last " + difference.ToString("0.00", CultureInfo.InvariantCulture) + " s never plays";
            }
            return "holds its last frame for " + (-difference).ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static void SetVatSourceClip(ClipAsset clip, AnimationClip sourceClip)
        {
            if (sourceClip != null)
            {
                if (clip.vatSource != null && clip.vatSource.sourceClip == sourceClip)
                {
                    return;
                }
                Undo.RecordObject(clip, "Set VAT source");
                if (clip.vatSource == null)
                {
                    clip.vatSource = new VatClipSource();
                }
                clip.vatSource.sourceClip = sourceClip;
                EditorUtility.SetDirty(clip);
                return;
            }
            if (clip.vatSource == null || clip.vatSource.sourceClip == null)
            {
                return;
            }
            Undo.RecordObject(clip, "Set VAT source");
            clip.vatSource.sourceClip = null;
            EditorUtility.SetDirty(clip);
        }

        private static void SetVatTrackSourceClip(ClipAsset clip, uint targetId, AnimationClip sourceClip)
        {
            VatTrack vatTrack = FindVatTrack(clip, targetId);
            if (sourceClip != null)
            {
                if (vatTrack != null && vatTrack.sourceClip == sourceClip)
                {
                    return;
                }
                Undo.RecordObject(clip, "Set VAT source");
                if (clip.vatTracks == null)
                {
                    clip.vatTracks = new List<VatTrack>();
                }
                if (vatTrack != null)
                {
                    vatTrack.sourceClip = sourceClip;
                }
                else
                {
                    clip.vatTracks.Add(new VatTrack { targetId = targetId, sourceClip = sourceClip });
                }
                EditorUtility.SetDirty(clip);
                return;
            }
            if (vatTrack == null)
            {
                return;
            }
            Undo.RecordObject(clip, "Set VAT source");
            clip.vatTracks.Remove(vatTrack);
            EditorUtility.SetDirty(clip);
        }

        private static VatTrack FindVatTrack(ClipAsset clip, uint targetId)
        {
            if (clip.vatTracks == null)
            {
                return null;
            }
            for (int trackIndex = 0; trackIndex < clip.vatTracks.Count; trackIndex++)
            {
                VatTrack vatTrack = clip.vatTracks[trackIndex];
                if (vatTrack != null && vatTrack.targetId == targetId)
                {
                    return vatTrack;
                }
            }
            return null;
        }
    }
}
