using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.Tests
{
    // Characterization tests for the design-driven pipeline's pure shape math (DesignApplyUtil):
    // stride-and-offset slice resolution across designs, tag-pool sizing (empty-tag double-count
    // avoidance), matched-design reporting, clamp-to-fallback, and the CharacterPalette capacity
    // guard. These pin the CURRENT behaviour.
    [TestFixture]
    public sealed class DesignApplyUtilTests
    {
        // Test def: one strided tagged design + one tag-independent (empty-tag) design covers every
        // documented edge:
        //   [0] "Pale"  min 0,  max 10, step 2 → 6 slices (0,2,4,6,8,10)
        //   [1] ""      min 20, max 22, step 1 → 3 slices (20,21,22)
        private static BlobAssetReference<PartDef> BuildTestDef()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref PartDef root = ref builder.ConstructRoot<PartDef>();
            root.id    = default;
            root.group = new FixedString32Bytes("Skin");

            BlobBuilderArray<PartDesignDef> designs = builder.Allocate(ref root.designs, 2);
            designs[0] = new PartDesignDef
            {
                tag             = new FixedString32Bytes("Pale"),
                minTextureIndex = 0,
                maxTextureIndex = 10,
                step            = 2,
            };
            designs[1] = new PartDesignDef
            {
                tag             = default,
                minTextureIndex = 20,
                maxTextureIndex = 22,
                step            = 1,
            };

            BlobAssetReference<PartDef> blob = builder.CreateBlobAssetReference<PartDef>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        [Test]
        public void TagPoolSize_CountsStridedDesignPlusEmptyTagDesigns()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                // 6 tagged slices + 3 tag-independent slices.
                Assert.AreEqual(9, DesignApplyUtil.TagPoolSize(ref blob.Value, paleTag));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void TagPoolSize_EmptyTagDoesNotDoubleCountTheEmptyDesigns()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                // Documented edge: empty tag counts the empty-tag designs ONCE (first loop only).
                Assert.AreEqual(3, DesignApplyUtil.TagPoolSize(ref blob.Value, default));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_HonoursTheDesignStrideAndReportsTheMatchedDesign()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(0,  DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 0, out int designAt0));
                Assert.AreEqual(2,  DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 1, out int designAt1));
                Assert.AreEqual(10, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 5, out int designAt5));
                Assert.AreEqual(0, designAt0);
                Assert.AreEqual(0, designAt1);
                Assert.AreEqual(0, designAt5);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_SpillsIntoEmptyTagDesignsAfterTaggedOnes()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(20, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 6, out int designAt6));
                Assert.AreEqual(22, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 8, out int designAt8));
                // The spilled slices come from the empty-tag design — its slots colour the part.
                Assert.AreEqual(1, designAt6);
                Assert.AreEqual(1, designAt8);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_PastTheEndClampsToFirstMatchedDesignMin()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(0, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 100, out int matchedDesign));
                Assert.AreEqual(0, matchedDesign);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_NegativeOffsetIsTreatedAsZero()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(0, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, -5, out int _));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_UnknownTagWithNoEmptyDesignsReturnsMinusOne()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref PartDef root = ref builder.ConstructRoot<PartDef>();
            root.id    = default;
            root.group = new FixedString32Bytes("Skin");
            BlobBuilderArray<PartDesignDef> designs = builder.Allocate(ref root.designs, 1);
            designs[0] = new PartDesignDef
            {
                tag             = new FixedString32Bytes("Pale"),
                minTextureIndex = 0,
                maxTextureIndex = 4,
                step            = 1,
            };
            BlobAssetReference<PartDef> blob = builder.CreateBlobAssetReference<PartDef>(Allocator.Persistent);
            builder.Dispose();
            try
            {
                FixedString32Bytes zombieTag = new FixedString32Bytes("Zombie");
                Assert.AreEqual(-1, DesignApplyUtil.SliceAtOffset(ref blob.Value, zombieTag, 0, out int matchedDesign));
                Assert.AreEqual(-1, matchedDesign);
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SetTag_UpsertsAnExistingGroupInsteadOfAppending()
        {
            FixedList512Bytes<PaletteEntry> groups = new FixedList512Bytes<PaletteEntry>();
            FixedString32Bytes skinGroup = new FixedString32Bytes("Skin");

            DesignApplyUtil.SetTag(ref groups, skinGroup, new FixedString32Bytes("Pale"));
            DesignApplyUtil.SetTag(ref groups, skinGroup, new FixedString32Bytes("Zombie"));

            Assert.AreEqual(1, groups.Length);
            Assert.AreEqual(new FixedString32Bytes("Zombie"), DesignApplyUtil.GetTag(groups, skinGroup));
        }

        [Test]
        public void GetTag_UnknownGroupReturnsEmpty()
        {
            FixedList512Bytes<PaletteEntry> groups = new FixedList512Bytes<PaletteEntry>();
            FixedString32Bytes missingGroup = new FixedString32Bytes("Hair");
            Assert.AreEqual(default(FixedString32Bytes), DesignApplyUtil.GetTag(groups, missingGroup));
        }

        [Test]
        public void SetTag_AtCapacityDropsTheNewGroupWithAWarningInsteadOfThrowing()
        {
            FixedList512Bytes<PaletteEntry> groups = new FixedList512Bytes<PaletteEntry>();
            int capacity = groups.Capacity;

            for (int groupIndex = 0; groupIndex < capacity; groupIndex++)
            {
                FixedString32Bytes groupName = new FixedString32Bytes();
                groupName.Append('G');
                groupName.Append(groupIndex);
                DesignApplyUtil.SetTag(ref groups, groupName, new FixedString32Bytes("Tag"));
            }
            Assert.AreEqual(capacity, groups.Length);

            LogAssert.Expect(LogType.Warning, new Regex("palette group capacity"));
            FixedString32Bytes overflowGroup = new FixedString32Bytes("Overflow");
            DesignApplyUtil.SetTag(ref groups, overflowGroup, new FixedString32Bytes("Tag"));

            Assert.AreEqual(capacity, groups.Length,
                "The over-capacity group must be dropped (with a warning), not appended or thrown.");
        }

        [Test]
        public void UpsertShape_OverwritesExistingTargetAndGetShapeIndexReadsItBack()
        {
            FixedList512Bytes<DesignSlot> slots = new FixedList512Bytes<DesignSlot>();
            DesignApplyUtil.UpsertShape(ref slots, 3, 5);
            DesignApplyUtil.UpsertShape(ref slots, 3, 9);

            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual(9, DesignApplyUtil.GetShapeIndex(slots, 3));
            Assert.AreEqual(0, DesignApplyUtil.GetShapeIndex(slots, 99), "Un-rolled targets default to 0.");
        }
    }
}
