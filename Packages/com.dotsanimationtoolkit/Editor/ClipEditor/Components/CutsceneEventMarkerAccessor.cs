// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Adapts one CutsceneEventMarker on a CutsceneAsset to IEventMarkerAccessor; time is already
    /// absolute seconds and the skip/hold flags are native to this marker type.</summary>
    public sealed class CutsceneEventMarkerAccessor : IEventMarkerAccessor
    {
        private readonly CutsceneAsset cutsceneAsset;

        public CutsceneEventMarkerAccessor(CutsceneAsset cutscene, int index)
        {
            this.cutsceneAsset = cutscene;
            this.Index = index;
        }

        public int Index { get; }

        public ScriptableObject OwnerAsset => this.cutsceneAsset;

        private CutsceneEventMarker Marker =>
            this.MarkerExists ? this.cutsceneAsset.events[this.Index] : null;

        public bool MarkerExists =>
            this.cutsceneAsset != null
            && this.cutsceneAsset.events != null
            && this.Index >= 0
            && this.Index < this.cutsceneAsset.events.Count
            && this.cutsceneAsset.events[this.Index] != null;

        public uint Key
        {
            get => this.MarkerExists ? this.Marker.eventKey : 0;
            set => this.WriteField(marker => marker.eventKey, (marker, newValue) => marker.eventKey = newValue, value);
        }

        public int IntParam
        {
            get => this.MarkerExists ? this.Marker.intParam : 0;
            set => this.WriteField(marker => marker.intParam, (marker, newValue) => marker.intParam = newValue, value);
        }

        public float FloatParam
        {
            get => this.MarkerExists ? this.Marker.floatParam : 0f;
            set => this.WriteField(marker => marker.floatParam, (marker, newValue) => marker.floatParam = newValue, value);
        }

        public float DisplayTimeSeconds
        {
            get => this.MarkerExists ? this.Marker.time : 0f;
            set => this.WriteField(marker => marker.time, (marker, newValue) => marker.time = newValue, Mathf.Max(0f, value));
        }

        public bool TimeIsClipRelative => false;

        public bool HasWindow => false;

        public float WindowSeconds
        {
            get => 0f;
            set { }
        }

        public bool HasSkipFlag => true;

        public bool FireOnSkip
        {
            get => this.MarkerExists && this.Marker.fireOnSkip;
            set => this.WriteField(marker => marker.fireOnSkip, (marker, newValue) => marker.fireOnSkip = newValue, value);
        }

        public bool HasHoldFlag => true;

        public bool HoldUntilReleased
        {
            get => this.MarkerExists && this.Marker.holdUntilReleased;
            set => this.WriteField(marker => marker.holdUntilReleased, (marker, newValue) => marker.holdUntilReleased = newValue, value);
        }

        private void WriteField<TValue>(
            System.Func<CutsceneEventMarker, TValue> read,
            System.Action<CutsceneEventMarker, TValue> write,
            TValue value)
        {
            if (!this.MarkerExists)
            {
                return;
            }

            CutsceneEventMarker marker = this.Marker;
            if (Equals(read(marker), value))
            {
                return;
            }

            Undo.RecordObject(this.cutsceneAsset, "Edit Event");
            write(marker, value);
            EditorUtility.SetDirty(this.cutsceneAsset);
        }
    }
}
