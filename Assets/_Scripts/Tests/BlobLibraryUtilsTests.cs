using System.Collections.Generic;
using NUnit.Framework;

namespace StitchPunk.Tests
{
    // BlobLibraryUtils is shared by every PostBakingSystemGroup library baker. The null-skip in
    // BuildEnumLookup and the enum-count are the two pure-C# pieces; if either regresses, every blob
    // library bakes wrong (missing or mis-sized slots) with no compile error.
    [TestFixture]
    public sealed class BlobLibraryUtilsTests
    {
        private enum SampleEnum
        {
            Alpha,
            Beta,
            Gamma
        }

        [Test]
        public void EnumCount_ReturnsDeclaredValueCount()
        {
            Assert.AreEqual(3, BlobLibraryUtils.EnumCount<SampleEnum>());
        }

        [Test]
        public void BuildEnumLookup_KeysByProvidedSelector()
        {
            string[] items = new string[] { "a", "bb", "ccc" };
            Dictionary<int, string> lookup =
                BlobLibraryUtils.BuildEnumLookup(items, (string entry) => entry.Length);

            Assert.AreEqual(3, lookup.Count);
            Assert.AreEqual("a", lookup[1]);
            Assert.AreEqual("bb", lookup[2]);
            Assert.AreEqual("ccc", lookup[3]);
        }

        [Test]
        public void BuildEnumLookup_SkipsNullEntries()
        {
            string[] items = new string[] { "a", null, "ccc" };
            Dictionary<int, string> lookup =
                BlobLibraryUtils.BuildEnumLookup(items, (string entry) => entry.Length);

            Assert.AreEqual(2, lookup.Count);
            Assert.IsFalse(lookup.ContainsValue(null));
        }

        [Test]
        public void BuildEnumLookup_LastWriterWinsOnKeyCollision()
        {
            // "bb" and "cc" both key to length 2; the later entry should overwrite.
            string[] items = new string[] { "bb", "cc" };
            Dictionary<int, string> lookup =
                BlobLibraryUtils.BuildEnumLookup(items, (string entry) => entry.Length);

            Assert.AreEqual(1, lookup.Count);
            Assert.AreEqual("cc", lookup[2]);
        }
    }
}
