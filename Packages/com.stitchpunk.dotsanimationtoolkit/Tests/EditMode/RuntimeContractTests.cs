// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode conformance of the C1 data slice against the architecture's normative tables:
    /// the section 6.2 [MaterialProperty] shader-property names, the section 5.2 buffer
    /// capacities and enableable set, the section 5.5 reserved event keys, the enum layouts the
    /// bake hash depends on (section 4.5), and the identity structs' contracts (section 3.4).
    /// </summary>
    public sealed class RuntimeContractTests
    {
        private const string MaterialPropertyAttributeFullName = "Unity.Rendering.MaterialPropertyAttribute";

        private static string ResolveMaterialPropertyName(Type componentType)
        {
            object[] attributes = componentType.GetCustomAttributes(false);
            foreach (object attribute in attributes)
            {
                Type attributeType = attribute.GetType();
                if (attributeType.FullName != MaterialPropertyAttributeFullName)
                {
                    continue;
                }
                PropertyInfo nameProperty = attributeType.GetProperty("Name");
                if (nameProperty != null)
                {
                    return (string)nameProperty.GetValue(attribute);
                }
                FieldInfo nameField = attributeType.GetField("Name");
                if (nameField != null)
                {
                    return (string)nameField.GetValue(attribute);
                }
            }
            return null;
        }

        [Test]
        public void MaterialPropertyComponents_DeclareTheExactSection62ShaderPropertyNames()
        {
            Assert.AreEqual("_ImageIndex", ResolveMaterialPropertyName(typeof(SpriteSliceProperty)));
            Assert.AreEqual("_AtlasFrame", ResolveMaterialPropertyName(typeof(AtlasFrameProperty)));
            Assert.AreEqual("_VatFrameA", ResolveMaterialPropertyName(typeof(VatFrameAProperty)));
            Assert.AreEqual("_VatFrameB", ResolveMaterialPropertyName(typeof(VatFrameBProperty)));
            Assert.AreEqual("_VatBlend", ResolveMaterialPropertyName(typeof(VatBlendProperty)));
            Assert.AreEqual("_BillboardParams", ResolveMaterialPropertyName(typeof(BillboardParamsProperty)));
        }

        [Test]
        public void MaterialPropertyComponents_ValueFieldTypesMatchTheSection62Table()
        {
            Assert.AreEqual(typeof(float), ResolveValueFieldType(typeof(SpriteSliceProperty)));
            Assert.AreEqual(typeof(float4), ResolveValueFieldType(typeof(AtlasFrameProperty)));
            Assert.AreEqual(typeof(float), ResolveValueFieldType(typeof(VatFrameAProperty)));
            Assert.AreEqual(typeof(float), ResolveValueFieldType(typeof(VatFrameBProperty)));
            Assert.AreEqual(typeof(float), ResolveValueFieldType(typeof(VatBlendProperty)));
            Assert.AreEqual(typeof(float4), ResolveValueFieldType(typeof(BillboardParamsProperty)));
        }

        /// <summary>
        /// Reads the <c>Value</c> field's type, asserting the field exists first. The [MaterialProperty]
        /// contract binds that exact field name, so a rename must surface as a readable failure here
        /// rather than as a NullReferenceException from a chained reflection call.
        /// </summary>
        private static Type ResolveValueFieldType(Type materialPropertyComponentType)
        {
            FieldInfo valueField = materialPropertyComponentType.GetField("Value");
            Assert.IsNotNull(valueField,
                materialPropertyComponentType.Name +
                " must expose a public 'Value' field for [MaterialProperty] instancing (section 6.2).");
            return valueField.FieldType;
        }

        private static int ResolveInternalBufferCapacity(Type bufferElementType)
        {
            InternalBufferCapacityAttribute capacityAttribute =
                (InternalBufferCapacityAttribute)Attribute.GetCustomAttribute(
                    bufferElementType, typeof(InternalBufferCapacityAttribute));
            Assert.IsNotNull(capacityAttribute,
                bufferElementType.Name + " must declare [InternalBufferCapacity] per architecture section 5.2.");
            return capacityAttribute.Capacity;
        }

        [Test]
        public void BufferElements_DeclareTheSection52InternalCapacities()
        {
            Assert.AreEqual(8, ResolveInternalBufferCapacity(typeof(PlaybackLayer)));
            Assert.AreEqual(4, ResolveInternalBufferCapacity(typeof(AnimationCommand)));
            Assert.AreEqual(4, ResolveInternalBufferCapacity(typeof(AnimEventOutput)));
            Assert.AreEqual(16, ResolveInternalBufferCapacity(typeof(RigPartRef)));
        }

        [Test]
        public void EnableableTags_ImplementIEnableableComponent()
        {
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(AnimationCommandPending)));
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(AnimEventsPending)));
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(RigBindingUninitialized)));
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(AnimVisible)));
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(BoundsDirty)));
        }

        [Test]
        public void ReservedEventKeys_MatchTheSection55Contract()
        {
            Assert.AreEqual(0u, (uint)ReservedEventKeys.Invalid);
            Assert.AreEqual(1u, (uint)ReservedEventKeys.ClipFinished);
            Assert.AreEqual(2u, (uint)ReservedEventKeys.ClipResolveFailed);
            Assert.AreEqual(16u, (uint)ReservedEventKeys.FirstUserKey);
        }

        [Test]
        public void LoopMode_MembersMatchTheContractOrder()
        {
            Assert.AreEqual(0, (byte)LoopMode.UseClipDefault);
            Assert.AreEqual(1, (byte)LoopMode.Once);
            Assert.AreEqual(2, (byte)LoopMode.Loop);
            Assert.AreEqual(3, (byte)LoopMode.PingPong);
        }

        [Test]
        public void Interpolation_MembersMatchTheContractOrder()
        {
            Assert.AreEqual(0, (byte)Interpolation.Linear);
            Assert.AreEqual(1, (byte)Interpolation.Step);
            Assert.AreEqual(2, (byte)Interpolation.EaseIn);
            Assert.AreEqual(3, (byte)Interpolation.EaseOut);
            Assert.AreEqual(4, (byte)Interpolation.EaseInOut);
        }

        [Test]
        public void AnimatedChannels_AreDistinctSingleBitFlags()
        {
            byte[] channelBits =
            {
                (byte)AnimatedChannels.PositionXY,
                (byte)AnimatedChannels.PositionZ,
                (byte)AnimatedChannels.Rotation,
                (byte)AnimatedChannels.Scale
            };
            byte combinedBits = 0;
            foreach (byte channelBit in channelBits)
            {
                Assert.AreNotEqual(0, channelBit, "Every channel needs a bit.");
                Assert.AreEqual(0, channelBit & (channelBit - 1), "Each channel must be a single bit.");
                Assert.AreEqual(0, combinedBits & channelBit, "Channel bits must not overlap.");
                combinedBits |= channelBit;
            }
        }

        [Test]
        public void PlaybackFlags_AreDistinctSingleBitFlags()
        {
            byte[] flagBits =
            {
                (byte)PlaybackFlags.Active,
                (byte)PlaybackFlags.Blending,
                (byte)PlaybackFlags.HasQueued,
                (byte)PlaybackFlags.Finished,
                (byte)PlaybackFlags.FinishedThisFrame
            };
            byte combinedBits = 0;
            foreach (byte flagBit in flagBits)
            {
                Assert.AreNotEqual(0, flagBit, "Every flag needs a bit.");
                Assert.AreEqual(0, flagBit & (flagBit - 1), "Each flag must be a single bit.");
                Assert.AreEqual(0, combinedBits & flagBit, "Flag bits must not overlap.");
                combinedBits |= flagBit;
            }
        }

        [Test]
        public void ClipId_ZeroIsReservedInvalid_AndEqualityFollowsTheValue()
        {
            Assert.IsFalse(default(ClipId).IsValid);
            Assert.IsFalse(new ClipId(0).IsValid);
            Assert.IsTrue(new ClipId(0xAB).IsValid);

            Assert.IsTrue(new ClipId(42).Equals(new ClipId(42)));
            Assert.IsTrue(new ClipId(42) == new ClipId(42));
            Assert.IsTrue(new ClipId(42) != new ClipId(43));
            Assert.AreEqual(new ClipId(42).GetHashCode(), new ClipId(42).GetHashCode());
        }

        [Test]
        public void ClipId_ComparisonOrdersByUnsignedValue_ForTheBinarySearchContract()
        {
            Assert.Less(new ClipId(2).CompareTo(new ClipId(9)), 0);
            Assert.Greater(new ClipId(100).CompareTo(new ClipId(9)), 0);
            Assert.AreEqual(0, new ClipId(9).CompareTo(new ClipId(9)));
            Assert.Greater(new ClipId(ulong.MaxValue).CompareTo(new ClipId(1)), 0,
                "Ordering must be unsigned across the full 64-bit range.");
        }

        [Test]
        public void ClipId_ToString_RendersSixteenHexDigits()
        {
            Assert.AreEqual("00000000000000AB", new ClipId(0xAB).ToString());
        }

        [Test]
        public void TargetId_ZeroIsReservedInvalid_AndComparisonOrdersByValue()
        {
            Assert.IsFalse(default(TargetId).IsValid);
            Assert.IsTrue(new TargetId(10).IsValid);
            Assert.IsTrue(new TargetId(10) == new TargetId(10));
            Assert.IsTrue(new TargetId(10) != new TargetId(11));
            Assert.Less(new TargetId(10).CompareTo(new TargetId(30)), 0);
            Assert.AreEqual("0000000A", new TargetId(10).ToString());
        }
    }
}
