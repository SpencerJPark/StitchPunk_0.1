// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>The cutscene clipboard's two load-bearing promises: relative times, and a part track found by tag.</summary>
    public sealed class CutsceneKeyClipboardTests
    {
        private CutsceneAsset cutscene;

        [SetUp]
        public void CreateCutscene()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            cutscene.slots.Add(new CutsceneSlot { name = "Source" });
            cutscene.slots.Add(new CutsceneSlot { name = "Destination" });
            cutscene.EnsureStableIds();
        }

        [TearDown]
        public void DestroyCutscene()
        {
            CutsceneKeyClipboard.Clear();
            Object.DestroyImmediate(cutscene);
        }

        private static CutsceneTransformKey KeyAt(float time)
        {
            return new CutsceneTransformKey
            {
                time = time,
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            };
        }

        [Test]
        public void Paste_AnchorsRelativeTimesAtThePlayhead()
        {
            cutscene.slots[0].transformKeys.Add(KeyAt(1f));
            cutscene.slots[0].transformKeys.Add(KeyAt(2.5f));

            List<CutsceneItemAddress> copied = new List<CutsceneItemAddress>
            {
                new CutsceneItemAddress(0, SelectedLaneKind.RootTransformKey, -1, 0),
                new CutsceneItemAddress(0, SelectedLaneKind.RootTransformKey, -1, 1)
            };
            Assert.AreEqual(2, CutsceneKeyClipboard.Copy(cutscene, copied));

            SerializedObject serializedCutscene = new SerializedObject(cutscene);
            int pastedCount = CutsceneKeyClipboard.Paste(cutscene, serializedCutscene, 5f, 1, null);
            serializedCutscene.ApplyModifiedProperties();

            Assert.AreEqual(2, pastedCount);
            List<CutsceneTransformKey> destinationKeys = cutscene.slots[1].transformKeys;
            Assert.AreEqual(2, destinationKeys.Count);
            Assert.AreEqual(5f, destinationKeys[0].time, 1e-5f, "The earliest copied key lands on the playhead.");
            Assert.AreEqual(6.5f, destinationKeys[1].time, 1e-5f, "The second keeps its 1.5s offset.");
            Assert.AreEqual(2, cutscene.slots[0].transformKeys.Count, "The source lane is untouched.");
        }

        [Test]
        public void Paste_PartTrack_CreatesTheTaggedTrackWhenMissing()
        {
            const uint jawTagId = 0xABCDEF01u;
            CutsceneKeyedTrack sourceTrack = new CutsceneKeyedTrack { tagId = jawTagId };
            sourceTrack.keys.Add(KeyAt(2f));
            cutscene.slots[0].partTracks.Add(sourceTrack);

            List<CutsceneItemAddress> copied = new List<CutsceneItemAddress>
            {
                new CutsceneItemAddress(0, SelectedLaneKind.PartTrackKey, 0, 0)
            };
            Assert.AreEqual(1, CutsceneKeyClipboard.Copy(cutscene, copied));
            Assert.AreEqual(0, cutscene.slots[1].partTracks.Count, "The destination has no tracks yet.");

            SerializedObject serializedCutscene = new SerializedObject(cutscene);
            int pastedCount = CutsceneKeyClipboard.Paste(cutscene, serializedCutscene, 4f, 1, null);
            serializedCutscene.ApplyModifiedProperties();

            Assert.AreEqual(1, pastedCount);
            Assert.AreEqual(1, cutscene.slots[1].partTracks.Count, "The tagged track was created.");
            Assert.AreEqual(jawTagId, cutscene.slots[1].partTracks[0].tagId, "It carries the source's tag.");
            Assert.AreEqual(1, cutscene.slots[1].partTracks[0].keys.Count);
            Assert.AreEqual(4f, cutscene.slots[1].partTracks[0].keys[0].time, 1e-5f);
        }
    }
}
