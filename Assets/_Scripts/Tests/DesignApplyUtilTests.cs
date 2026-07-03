using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.Tests
{
    // Characterization tests for the tag-driven design pipeline's pure math (DesignApplyUtil):
    // stride-and-offset slice resolution, tag-pool sizing (empty-tag double-count avoidance),
    // clamp-to-fallback, and the CharacterPalette capacity guard. These pin the CURRENT behaviour.
    [TestFixture]
    public sealed class DesignApplyUtilTests
    {
        // Test def: two "Pale" ranges is overkill — one strided tagged range + one colour-independent
        // (empty-tag) range covers every documented edge:
        //   "Pale"  min 0,  max 10, step 2 → 6 slices (0,2,4,6,8,10)
        //   ""      min 20, max 22, step 1 → 3 slices (20,21,22)
        private static BlobAssetReference<PartDef> BuildTestDef()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref PartDef root = ref builder.ConstructRoot<PartDef>();
            root.id                 = default;
            root.group              = new FixedString32Bytes("Skin");
            root.defaultSettleSpeed = 8f;

            BlobBuilderArray<PartTagRange> ranges = builder.Allocate(ref root.ranges, 2);
            ranges[0] = new PartTagRange
            {
                tag          = new FixedString32Bytes("Pale"),
                min          = 0,
                max          = 10,
                step         = 2,
                randomizable = true,
            };
            ranges[1] = new PartTagRange
            {
                tag          = default,
                min          = 20,
                max          = 22,
                step         = 1,
                randomizable = true,
            };
            builder.Allocate(ref root.zones, 0);

            BlobAssetReference<PartDef> blob = builder.CreateBlobAssetReference<PartDef>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        [Test]
        public void TagPoolSize_CountsStridedRangePlusEmptyTagRanges()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                // 6 tagged slices + 3 colour-independent slices.
                Assert.AreEqual(9, DesignApplyUtil.TagPoolSize(ref blob.Value, paleTag));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void TagPoolSize_EmptyTagDoesNotDoubleCountTheEmptyRanges()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                // Documented edge: empty tag counts the empty-tag ranges ONCE (first loop only).
                Assert.AreEqual(3, DesignApplyUtil.TagPoolSize(ref blob.Value, default));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_HonoursTheRangeStride()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(0,  DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 0));
                Assert.AreEqual(2,  DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 1));
                Assert.AreEqual(10, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 5));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_SpillsIntoEmptyTagRangesAfterTaggedOnes()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(20, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 6));
                Assert.AreEqual(22, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 8));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_PastTheEndClampsToFirstMatchedRangeMin()
        {
            BlobAssetReference<PartDef> blob = BuildTestDef();
            try
            {
                FixedString32Bytes paleTag = new FixedString32Bytes("Pale");
                Assert.AreEqual(0, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, 100));
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
                Assert.AreEqual(0, DesignApplyUtil.SliceAtOffset(ref blob.Value, paleTag, -5));
            }
            finally { blob.Dispose(); }
        }

        [Test]
        public void SliceAtOffset_UnknownTagWithNoEmptyRangesReturnsMinusOne()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref PartDef root = ref builder.ConstructRoot<PartDef>();
            root.id                 = default;
            root.group              = new FixedString32Bytes("Skin");
            root.defaultSettleSpeed = 8f;
            BlobBuilderArray<PartTagRange> ranges = builder.Allocate(ref root.ranges, 1);
            ranges[0] = new PartTagRange
            {
                tag          = new FixedString32Bytes("Pale"),
                min          = 0,
                max          = 4,
                step         = 1,
                randomizable = true,
            };
            builder.Allocate(ref root.zones, 0);
            BlobAssetReference<PartDef> blob = builder.CreateBlobAssetReference<PartDef>(Allocator.Persistent);
            builder.Dispose();
            try
            {
                FixedString32Bytes zombieTag = new FixedString32Bytes("Zombie");
                Assert.AreEqual(-1, DesignApplyUtil.SliceAtOffset(ref blob.Value, zombieTag, 0));
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
