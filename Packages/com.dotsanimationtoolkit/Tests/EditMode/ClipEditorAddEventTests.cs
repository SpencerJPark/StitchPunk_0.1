// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Reflection;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <c>ClipEditorWindow.AddEventAtPlayhead</c> — the transport bar's Add
    /// Event button (Phase D13, Task 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exercises the real private method through reflection, following the pattern
    /// <c>ClipEditorHierarchySelectionTests</c> established: a bare
    /// <c>ScriptableObject.CreateInstance&lt;ClipEditorWindow&gt;()</c> never calls
    /// <c>CreateGUI</c>, so every UI Toolkit field the method touches is null — and every method it
    /// calls along the way (<c>RebuildTimeline</c>, <c>SyncTransportPlayhead</c>,
    /// <c>RefreshLiveInspectorValues</c>) already null-checks its own UI references for exactly this
    /// reason, so the call is safe without a window on screen.
    /// </para>
    /// <para>
    /// The two things worth pinning down are the two things most likely to regress silently: that a
    /// clip with zero events still gets a marker (the events lane is gated on
    /// <c>events != null</c>, not <c>events.Count &gt; 0</c>, so this should already work — this test
    /// is what proves it rather than assumes it), and that the newly added marker's selection
    /// survives <c>SortTrackKeys</c> when the playhead lands before an existing marker and the sort
    /// moves it.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorAddEventTests
    {
        [Test]
        public void AddEventAtPlayhead_ClipWithNoEvents_AddsFirstMarkerAtPlayheadAndSelectsIt()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            try
            {
                SetSelectedClip(window, clip);
                SetPlayheadTime(window, 0.4f);

                InvokeAddEventAtPlayhead(window);

                Assert.AreEqual(
                    1, clip.events.Count,
                    "The events lane is gated on events != null, not events.Count > 0, so a clip "
                        + "that starts with zero markers must still gain one.");
                Assert.AreEqual(
                    0.4f, clip.events[0].normalizedTime, 1e-5f,
                    "With no snap toggle bound (a bare window has none), the marker lands exactly "
                        + "on the playhead.");
                Assert.AreEqual(
                    AnimEventMaskKeys.FirstMaskKey, clip.events[0].eventKey,
                    "No clip set is assigned, so the new marker falls back to the first legal user "
                        + "key, same as the double-click add path.");
                Assert.AreEqual(0f, clip.events[0].windowSeconds, "Pulse-only by default.");

                KeyAddress expectedAddress = new KeyAddress(TimelineTrackKind.Event, 0, 0);
                HashSet<KeyAddress> selectedKeys = GetSelectedKeys(window);
                Assert.AreEqual(1, selectedKeys.Count);
                Assert.IsTrue(
                    selectedKeys.Contains(expectedAddress),
                    "The button's whole reason to exist is skipping the hunt for the marker it just "
                        + "made — it must select what it added, unlike double-click add.");
                Assert.IsTrue(GetHasActiveKey(window), "The inspector reads the active key, not just the set.");
                Assert.AreEqual(expectedAddress, GetActiveKey(window));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
                ScriptableObject.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AddEventAtPlayhead_PlayheadBeforeExistingMarkerOnTheSameLane_SelectionFollowsThroughTheSort()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            try
            {
                // No clip set is assigned, so the new marker falls back to AnimEventMaskKeys.FirstMaskKey
                // (see the first test) — matching the first marker's key here is what puts the new
                // marker on the SAME lane (E6 Task 2) as an existing one, so its sort is a real
                // reorder rather than a trivial single-member one.
                clip.events.Add(
                    new EventMarker { normalizedTime = 0.2f, eventKey = AnimEventMaskKeys.FirstMaskKey });
                clip.events.Add(new EventMarker { normalizedTime = 0.8f, eventKey = 21u });

                SetSelectedClip(window, clip);
                SetPlayheadTime(window, 0.05f);

                InvokeAddEventAtPlayhead(window);

                Assert.AreEqual(3, clip.events.Count);
                // Each event lane sorts only its own flat slots (E6 Task 2) — the shared-key lane's
                // two markers (originally flat 0 and the newly appended flat 2) swap into ascending
                // time order, while the other lane's marker (flat 1) is untouched.
                Assert.AreEqual(0.05f, clip.events[0].normalizedTime, 1e-5f);
                Assert.AreEqual(0.8f, clip.events[1].normalizedTime, 1e-5f);
                Assert.AreEqual(0.2f, clip.events[2].normalizedTime, 1e-5f);

                KeyAddress expectedAddress = new KeyAddress(TimelineTrackKind.Event, 0, 0);
                HashSet<KeyAddress> selectedKeys = GetSelectedKeys(window);
                Assert.AreEqual(
                    1, selectedKeys.Count,
                    "Only the new marker should be selected, not whatever was selected before.");
                Assert.IsTrue(
                    selectedKeys.Contains(expectedAddress),
                    "AddEventAtPlayhead selects the new marker by its lane-local index before the "
                        + "sort runs — SortTrackKeys's index remap is what has to carry that "
                        + "selection to local index 0, where the marker actually landed.");
                Assert.AreEqual(expectedAddress, GetActiveKey(window));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
                ScriptableObject.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AddEventAtPlayhead_NoClipSelected_DoesNothing()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            try
            {
                // selectedClip is left at its default (null) — the state the button is disabled in.
                InvokeAddEventAtPlayhead(window);

                Assert.IsFalse(
                    GetHasActiveKey(window),
                    "With no clip, the method must return before touching any selection state.");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
            }
        }

        private static void SetSelectedClip(ClipEditorWindow window, ClipAsset clip)
        {
            typeof(ClipEditorWindow)
                .GetField("selectedClip", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(window, clip);
        }

        private static void SetPlayheadTime(ClipEditorWindow window, float normalizedTime)
        {
            typeof(ClipEditorWindow)
                .GetField("playheadTime", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(window, normalizedTime);
        }

        private static void InvokeAddEventAtPlayhead(ClipEditorWindow window)
        {
            typeof(ClipEditorWindow)
                .GetMethod("AddEventAtPlayhead", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, null);
        }

        private static HashSet<KeyAddress> GetSelectedKeys(ClipEditorWindow window)
        {
            return (HashSet<KeyAddress>)typeof(ClipEditorWindow)
                .GetField("selectedKeys", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(window);
        }

        private static bool GetHasActiveKey(ClipEditorWindow window)
        {
            return (bool)typeof(ClipEditorWindow)
                .GetField("hasActiveKey", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(window);
        }

        private static KeyAddress GetActiveKey(ClipEditorWindow window)
        {
            return (KeyAddress)typeof(ClipEditorWindow)
                .GetField("activeKey", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(window);
        }
    }
}
