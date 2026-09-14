// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Foldout strip showing every playback layer's active clip as an event lane, refreshed once per composer tick.
    /// </summary>
    public sealed class LayerEventStripElement : VisualElement, IDisposable
    {
        private const string ExpandedPrefKey = "DotsAnimationToolkit.ActorEditor.LayerEventStripExpanded";
        private const float RowHeight = 22f;
        private const float InactiveAlpha = 0.35f;
        private const long FlashDurationMilliseconds = 120;
        private const float FlashOutlineWidth = 3f;
        private const float NameLabelWidth = 72f;
        private const float ReadoutLabelWidth = 96f;

        private static readonly Color PlayheadColor = new Color(0.95f, 0.36f, 0.30f);

        private readonly Foldout foldout;
        private readonly List<LayerEventRowElement> rows;

        private ActorProfileAsset profile;
        private ActorPreviewComposer composer;
        private EditorEventPreviewPlayer previewPlayer;

        public LayerEventStripElement()
        {
            name = "layer-event-strip";
            style.flexShrink = 0f;
            style.marginTop = 4f;
            style.display = DisplayStyle.None;

            rows = new List<LayerEventRowElement>();

            foldout = new Foldout
            {
                text = "Layer Events",
                value = EditorPrefs.GetBool(ExpandedPrefKey, true)
            };
            foldout.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.target == foldout)
                {
                    EditorPrefs.SetBool(ExpandedPrefKey, changeEvent.newValue);
                }
            });
            Add(foldout);
        }

        public void Bind(ActorProfileAsset profileAsset, ActorPreviewComposer previewComposer)
        {
            profile = profileAsset;
            composer = previewComposer;

            foreach (LayerEventRowElement row in rows)
            {
                foldout.Remove(row);
            }
            rows.Clear();
        }

        public void Tick(bool isComposerPlaying)
        {
            int layerCount = composer != null && composer.IsCreated ? composer.LayerCount : 0;
            style.display = layerCount == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (layerCount == 0)
            {
                return;
            }

            if (layerCount != rows.Count)
            {
                foreach (LayerEventRowElement row in rows)
                {
                    foldout.Remove(row);
                }
                rows.Clear();

                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    LayerEventRowElement row = new LayerEventRowElement(this, () => profile, layerIndex);
                    foldout.Add(row);
                    rows.Add(row);
                }
            }

            for (int layerIndex = 0; layerIndex < rows.Count; layerIndex++)
            {
                string layerDisplayName = profile != null && profile.layers != null && layerIndex < profile.layers.Count
                    ? profile.layers[layerIndex].displayName
                    : "Layer " + layerIndex;
                rows[layerIndex].Refresh(composer.Layer(layerIndex), layerDisplayName, isComposerPlaying, foldout.value);
            }
        }

        public void Dispose()
        {
            if (previewPlayer != null)
            {
                previewPlayer.Dispose();
                previewPlayer = null;
            }
        }

        private void PlayPreviewSound(uint eventKey)
        {
            AnimEventKeyRegistry eventRegistry = ClipInspectorPane.ResolveEventKeyRegistry();
            AudioClip previewClip = eventRegistry != null ? eventRegistry.FindPreviewClip(eventKey) : null;
            if (previewClip == null)
            {
                return;
            }

            if (previewPlayer == null)
            {
                previewPlayer = new EditorEventPreviewPlayer();
            }
            previewPlayer.Play(previewClip);
        }

        private sealed class LayerEventRowElement : VisualElement
        {
            private readonly LayerEventStripElement owner;
            private readonly Func<ActorProfileAsset> profileProvider;
            private readonly Label nameLabel;
            private readonly VisualElement lane;
            private readonly Label readoutLabel;
            private readonly Dictionary<int, double> flashExpiryByMarkerIndex;
            private readonly List<int> crossedMarkerIndices;

            private ClipId boundClip;
            private ClipId boundPreviousClip;
            private ClipAsset currentClip;
            private ClipAsset previousClip;
            private LayerEventRowState state;
            private float lastNormalizedTime;
            private bool hasLastNormalizedTime;
            private int shownTimeCentiseconds;
            private int shownDurationCentiseconds = -1;
            private bool hasBoundOnce;
            private float lastAppliedOpacity = -1f;

            public LayerEventRowElement(LayerEventStripElement owningStrip, Func<ActorProfileAsset> profileGetter, int layerIndex)
            {
                owner = owningStrip;
                profileProvider = profileGetter;
                name = "layer-event-row-" + layerIndex;

                flashExpiryByMarkerIndex = new Dictionary<int, double>();
                crossedMarkerIndices = new List<int>();

                style.height = RowHeight;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;

                nameLabel = new Label { style = { width = NameLabelWidth, overflow = Overflow.Hidden } };
                Add(nameLabel);

                lane = new VisualElement { style = { flexGrow = 1f, height = RowHeight } };
                lane.generateVisualContent += DrawLane;
                Add(lane);

                readoutLabel = new Label { style = { width = ReadoutLabelWidth, unityTextAlign = TextAnchor.MiddleRight } };
                Add(readoutLabel);
            }

            public void Refresh(PlaybackLayer layer, string displayName, bool isComposerPlaying, bool isExpanded)
            {
                if (nameLabel.text != displayName)
                {
                    nameLabel.text = displayName;
                }

                // Asset lookups only fire on a clip change, never per tick.
                if (!hasBoundOnce || layer.clip != boundClip)
                {
                    boundClip = layer.clip;
                    currentClip = LayerEventRowResolver.FindClipAsset(profileProvider(), layer.clip);
                    hasLastNormalizedTime = false;
                    flashExpiryByMarkerIndex.Clear();
                    hasBoundOnce = true;
                }
                if (layer.previousClip != boundPreviousClip)
                {
                    boundPreviousClip = layer.previousClip;
                    previousClip = LayerEventRowResolver.FindClipAsset(profileProvider(), layer.previousClip);
                }

                LayerEventRowState resolvedState = LayerEventRowResolver.Resolve(layer, currentClip, previousClip);

                if (isExpanded && resolvedState.emits && hasLastNormalizedTime && currentClip != null
                    && currentClip.events != null && currentClip.events.Count > 0)
                {
                    crossedMarkerIndices.Clear();
                    ScrubEventCrossingResolver.Resolve(
                        lastNormalizedTime, resolvedState.normalizedTime, isComposerPlaying,
                        resolvedState.resolvedLoop, currentClip.events, crossedMarkerIndices);
                    for (int crossedIndex = 0; crossedIndex < crossedMarkerIndices.Count; crossedIndex++)
                    {
                        int markerIndex = crossedMarkerIndices[crossedIndex];
                        flashExpiryByMarkerIndex[markerIndex] = EditorApplication.timeSinceStartup + FlashDurationMilliseconds / 1000.0;
                        lane.schedule.Execute(lane.MarkDirtyRepaint).StartingIn(FlashDurationMilliseconds);
                        owner.PlayPreviewSound(currentClip.events[markerIndex].eventKey);
                    }
                }

                lastNormalizedTime = resolvedState.normalizedTime;
                hasLastNormalizedTime = currentClip != null;
                state = resolvedState;

                float rowAlpha = state.emits ? 1f : InactiveAlpha;
                if (rowAlpha != lastAppliedOpacity)
                {
                    nameLabel.style.opacity = rowAlpha;
                    readoutLabel.style.opacity = rowAlpha;
                    lastAppliedOpacity = rowAlpha;
                }

                if (currentClip == null)
                {
                    if (readoutLabel.text != "–")
                    {
                        readoutLabel.text = "–";
                        shownTimeCentiseconds = -1;
                        shownDurationCentiseconds = -1;
                    }
                }
                else
                {
                    int timeCentiseconds = Mathf.RoundToInt(state.time * 100f);
                    int durationCentiseconds = Mathf.RoundToInt(state.duration * 100f);
                    if (timeCentiseconds != shownTimeCentiseconds || durationCentiseconds != shownDurationCentiseconds)
                    {
                        readoutLabel.text = state.time.ToString("0.00") + " / " + state.duration.ToString("0.00") + " s";
                        shownTimeCentiseconds = timeCentiseconds;
                        shownDurationCentiseconds = durationCentiseconds;
                    }
                }

                if (isExpanded)
                {
                    lane.MarkDirtyRepaint();
                }
            }

            private void DrawLane(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                Rect rect = lane.contentRect;
                if (rect.width < 1f)
                {
                    return;
                }

                float centreY = rect.height * 0.5f;
                float usableWidth = Mathf.Max(0f, rect.width - 2f * EventLaneStyle.PinHalfWidth);

                painter.strokeColor = ToolkitPalette.BoxBorder;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, centreY));
                painter.LineTo(new Vector2(rect.width, centreY));
                painter.Stroke();

                if (state.ghostPrevious && previousClip != null && previousClip.events != null)
                {
                    for (int markerIndex = 0; markerIndex < previousClip.events.Count; markerIndex++)
                    {
                        EventMarker marker = previousClip.events[markerIndex];
                        Color eventColor = ToolkitPalette.ColorForEventKey(marker.eventKey);
                        float pinX = NormalizedTimeToX(marker.normalizedTime, usableWidth);
                        EventLaneStyle.DrawPin(
                            painter, pinX, centreY,
                            new Color(eventColor.r, eventColor.g, eventColor.b, 0f),
                            new Color(eventColor.r, eventColor.g, eventColor.b, InactiveAlpha), 1f);
                    }
                }

                if (currentClip != null && currentClip.events != null)
                {
                    float markerAlpha = state.emits ? 1f : InactiveAlpha;
                    for (int markerIndex = 0; markerIndex < currentClip.events.Count; markerIndex++)
                    {
                        EventMarker marker = currentClip.events[markerIndex];
                        Color eventColor = ToolkitPalette.ColorForEventKey(marker.eventKey);
                        float pinX = NormalizedTimeToX(marker.normalizedTime, usableWidth);
                        bool isFlashingMarker = flashExpiryByMarkerIndex.TryGetValue(markerIndex, out double flashExpiry)
                            && flashExpiry > EditorApplication.timeSinceStartup;
                        if (isFlashingMarker)
                        {
                            EventLaneStyle.DrawPin(painter, pinX, centreY, eventColor, eventColor, FlashOutlineWidth);
                        }
                        else
                        {
                            EventLaneStyle.DrawPin(
                                painter, pinX, centreY,
                                new Color(eventColor.r, eventColor.g, eventColor.b, markerAlpha),
                                new Color(EventLaneStyle.PinOutline.r, EventLaneStyle.PinOutline.g, EventLaneStyle.PinOutline.b, markerAlpha),
                                1f);
                        }
                    }
                }

                if (currentClip != null)
                {
                    float playheadAlpha = state.emits ? 1f : InactiveAlpha;
                    float playheadX = NormalizedTimeToX(state.normalizedTime, usableWidth);
                    painter.strokeColor = new Color(PlayheadColor.r, PlayheadColor.g, PlayheadColor.b, playheadAlpha);
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(playheadX, 0f));
                    painter.LineTo(new Vector2(playheadX, rect.height));
                    painter.Stroke();
                }
            }

            private static float NormalizedTimeToX(float normalizedTime, float usableWidth)
            {
                return EventLaneStyle.PinHalfWidth + normalizedTime * usableWidth;
            }
        }
    }
}
