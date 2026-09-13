// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Adapts one flat-indexed event on a ClipAsset to IEventMarkerAccessor, converting between the
    /// stored normalized time and seconds through the clip's duration.</summary>
    public sealed class ClipEventMarkerAccessor : IEventMarkerAccessor
    {
        private readonly ClipAsset clipAsset;

        public ClipEventMarkerAccessor(ClipAsset clip, int flatIndex)
        {
            this.clipAsset = clip;
            this.FlatIndex = flatIndex;
        }

        public int FlatIndex { get; }

        public ScriptableObject OwnerAsset => this.clipAsset;

        public bool MarkerExists =>
            this.clipAsset != null
            && this.clipAsset.events != null
            && this.FlatIndex >= 0
            && this.FlatIndex < this.clipAsset.events.Count;

        public uint Key
        {
            get => this.MarkerExists ? this.clipAsset.events[this.FlatIndex].eventKey : 0;
            set => this.EditMarker(marker =>
            {
                marker.eventKey = value;
                return marker;
            });
        }

        public int IntParam
        {
            get => this.MarkerExists ? this.clipAsset.events[this.FlatIndex].intParam : 0;
            set => this.EditMarker(marker =>
            {
                marker.intParam = value;
                return marker;
            });
        }

        public float FloatParam
        {
            get => this.MarkerExists ? this.clipAsset.events[this.FlatIndex].floatParam : 0f;
            set => this.EditMarker(marker =>
            {
                marker.floatParam = value;
                return marker;
            });
        }

        public float DisplayTimeSeconds
        {
            get => this.MarkerExists ? this.clipAsset.events[this.FlatIndex].normalizedTime * this.clipAsset.duration : 0f;
            set => this.EditMarker(marker =>
            {
                marker.normalizedTime = this.clipAsset.duration > 0f
                    ? Mathf.Clamp01(value / this.clipAsset.duration)
                    : 0f;
                return marker;
            });
        }

        public bool TimeIsClipRelative => true;

        public bool HasWindow => true;

        public float WindowSeconds
        {
            get => this.MarkerExists ? this.clipAsset.events[this.FlatIndex].windowSeconds : 0f;
            set => this.EditMarker(marker =>
            {
                marker.windowSeconds = Mathf.Max(0f, value);
                return marker;
            });
        }

        public bool HasSkipFlag => false;

        public bool FireOnSkip
        {
            get => false;
            set { }
        }

        public bool HasHoldFlag => false;

        public bool HoldUntilReleased
        {
            get => false;
            set { }
        }

        private void EditMarker(Func<EventMarker, EventMarker> edit)
        {
            if (!this.MarkerExists)
            {
                return;
            }

            EventMarker original = this.clipAsset.events[this.FlatIndex];
            EventMarker edited = edit(original);
            if (edited.Equals(original))
            {
                return;
            }

            Undo.RecordObject(this.clipAsset, "Edit Event");
            this.clipAsset.events[this.FlatIndex] = edited;
            EditorUtility.SetDirty(this.clipAsset);
        }
    }
}
