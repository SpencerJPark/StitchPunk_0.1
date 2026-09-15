using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class FlipbookValidationTests
    {
        [Test]
        public void SizeMismatches_NameEveryOffender()
        {
            List<(string name, Vector2Int size)> frames = new List<(string name, Vector2Int size)>
            {
                ("a", new Vector2Int(4, 4)),
                ("b", new Vector2Int(8, 8)),
                ("c", new Vector2Int(4, 4)),
                ("d", new Vector2Int(2, 2)),
            };

            List<string> mismatchedFrameNames = FlipbookValidation.FindSizeMismatches(frames, new Vector2Int(4, 4));

            Assert.AreEqual(2, mismatchedFrameNames.Count);
            Assert.AreEqual("b", mismatchedFrameNames[0]);
            Assert.AreEqual("d", mismatchedFrameNames[1]);
        }

        [Test]
        public void DedupeFrameName_AppendsCounter()
        {
            List<string> takenNames = new List<string>();

            string firstName = FlipbookValidation.DedupeFrameName("head", takenNames);
            takenNames.Add(firstName);
            string secondName = FlipbookValidation.DedupeFrameName("head", takenNames);
            takenNames.Add(secondName);
            string thirdName = FlipbookValidation.DedupeFrameName("head", takenNames);
            takenNames.Add(thirdName);

            Assert.AreEqual("head", firstName);
            Assert.AreEqual("head 1", secondName);
            Assert.AreEqual("head 2", thirdName);
        }
    }
}
