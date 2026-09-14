// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="LayerEventRowResolver"/>: the runtime's emit gate, the
    /// crossfade ghost, and the loop-resolved playhead.
    /// </summary>
    public sealed class LayerEventRowResolverTests
    {
        private ClipAsset currentClip;
        private ClipAsset previousClip;

        [SetUp]
        public void SetUp()
        {
            currentClip = ScriptableObject.CreateInstance<ClipAsset>();
            previousClip = ScriptableObject.CreateInstance<ClipAsset>();
            currentClip.duration = 1f;
            currentClip.defaultLoop = LoopMode.Loop;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(currentClip);
            Object.DestroyImmediate(previousClip);
        }

        [Test]
        public void InactiveLayer_DoesNotEmit_ButAFinishingLayerStillDoes()
        {
            PlaybackLayer stoppedLayer = new PlaybackLayer { clipIndex = 0, previousClipIndex = -1, flags = PlaybackFlags.None };
            Assert.IsFalse(LayerEventRowResolver.Resolve(stoppedLayer, currentClip, null).emits, "a layer without Active must not emit.");

            PlaybackLayer finishingLayer = new PlaybackLayer { clipIndex = 0, previousClipIndex = -1, flags = PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame };
            Assert.IsTrue(LayerEventRowResolver.Resolve(finishingLayer, currentClip, null).emits, "a layer on its finishing frame still emits, as the runtime does.");
        }

        [Test]
        public void BlendingLayer_GhostsPrevious_KeyedOnTheBlendingFlag()
        {
            PlaybackLayer blendingLayer = new PlaybackLayer { clipIndex = 0, previousClipIndex = 3, blendDuration = 0.2f, flags = PlaybackFlags.Active | PlaybackFlags.Blending };
            Assert.IsTrue(LayerEventRowResolver.Resolve(blendingLayer, currentClip, previousClip).ghostPrevious, "a crossfading layer must ghost its previous clip.");

            PlaybackLayer unflaggedLayer = new PlaybackLayer { clipIndex = 0, previousClipIndex = 3, blendDuration = 0.2f, flags = PlaybackFlags.Active };
            Assert.IsFalse(LayerEventRowResolver.Resolve(unflaggedLayer, currentClip, previousClip).ghostPrevious, "blendDuration alone must not ghost; the runtime keys on Blending.");
        }

        [Test]
        public void UseClipDefaultLoop_WrapsUnwrappedTimeOntoTheClip()
        {
            PlaybackLayer loopingLayer = new PlaybackLayer { clipIndex = 0, previousClipIndex = -1, loop = LoopMode.UseClipDefault, time = 2.25f, flags = PlaybackFlags.Active };
            LayerEventRowState state = LayerEventRowResolver.Resolve(loopingLayer, currentClip, null);
            Assert.AreEqual(0.25f, state.normalizedTime, 0.0001f, "UseClipDefault must resolve to the clip's Loop and wrap 2.25 s onto a 1 s clip.");
        }
    }
}
