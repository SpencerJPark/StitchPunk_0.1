using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Characterizes VatPreviewPlayback's frame resolution and advance/loop behavior against the runtime formula it mirrors.</summary>
    public class VatPreviewPlaybackTests
    {
        private static VatClipRange CreateRange()
        {
            return new VatClipRange
            {
                frameStart = 100,
                frameCount = 10,
                fps = 30f,
            };
        }

        [Test]
        public void GlobalFrame_MirrorsTheRuntimeFormula()
        {
            VatClipRange range = CreateRange();
            VatPreviewPlayback playback = new VatPreviewPlayback();
            playback.SetRange(range);
            Assert.AreEqual(100f, playback.GlobalFrame, 1e-4f);

            playback.Time = 0.1f;
            Assert.AreEqual(103f, playback.GlobalFrame, 1e-4f); // 100 + clamp(0.1*30, 0, 9)

            playback.Time = 10f; // far past Duration (10/30 = 0.333s) - Time setter must clamp
            Assert.AreEqual(109f, playback.GlobalFrame, 1e-4f); // 100 + clamp(huge, 0, 9) = 109
        }

        [Test]
        public void Advance_LoopsBackToTheStart_ButStopsAtTheEndWhenNotLooping()
        {
            VatClipRange range = CreateRange();

            VatPreviewPlayback loopingPlayback = new VatPreviewPlayback();
            loopingPlayback.SetRange(range);
            loopingPlayback.Loop = true;
            bool loopingResult = loopingPlayback.Advance(loopingPlayback.Duration + 0.05f);
            Assert.IsTrue(loopingResult);
            Assert.Less(loopingPlayback.Time, loopingPlayback.Duration);

            VatPreviewPlayback clampingPlayback = new VatPreviewPlayback();
            clampingPlayback.SetRange(range);
            clampingPlayback.Loop = false;
            bool clampingResult = clampingPlayback.Advance(clampingPlayback.Duration + 0.05f);
            Assert.IsFalse(clampingResult);
            Assert.AreEqual(clampingPlayback.Duration, clampingPlayback.Time, 1e-4f);
        }
    }
}
