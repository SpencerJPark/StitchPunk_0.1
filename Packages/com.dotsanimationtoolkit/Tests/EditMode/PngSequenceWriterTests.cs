// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers the capture frame file naming.</summary>
    public sealed class PngSequenceWriterTests
    {
        [Test]
        public void FrameFileName_PadsToFourDigits()
        {
            Assert.AreEqual("Walk_0007.png", PngSequenceWriter.FrameFileName("Walk", 7));
        }
    }
}
