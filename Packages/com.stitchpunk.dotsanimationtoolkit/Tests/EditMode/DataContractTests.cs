// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode conformance of the C1 data slice's layouts against the architecture's normative
    /// sketches: every blob struct of section 4.2 is field-exact, every runtime component and
    /// buffer element of section 5.2 exists with the contracted field set, and the section 4.2
    /// hard constraint that textures never live in blobs holds structurally.
    ///
    /// Field sets are compared by name and type rather than by declaration order: reflection does
    /// not guarantee ordering, and the bake hash of section 4.5 is defined by an explicit append
    /// order, not by struct field order.
    /// </summary>
    public sealed class DataContractTests
    {
        private struct FieldContract
        {
            public string fieldName;
            public Type fieldType;
        }

        private static FieldContract Field(string fieldName, Type fieldType)
        {
            return new FieldContract { fieldName = fieldName, fieldType = fieldType };
        }

        private static void AssertFieldsMatch(Type declaringType, FieldContract[] expectedFields)
        {
            FieldInfo[] actualFields = declaringType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Dictionary<string, Type> actualFieldTypes = new Dictionary<string, Type>();
            foreach (FieldInfo actualField in actualFields)
            {
                actualFieldTypes.Add(actualField.Name, actualField.FieldType);
            }

            Assert.AreEqual(
                expectedFields.Length,
                actualFieldTypes.Count,
                declaringType.Name + " must declare exactly the contracted public instance fields.");

            foreach (FieldContract expectedField in expectedFields)
            {
                Assert.IsTrue(
                    actualFieldTypes.ContainsKey(expectedField.fieldName),
                    declaringType.Name + " is missing the contracted field '" + expectedField.fieldName + "'.");
                Assert.AreEqual(
                    expectedField.fieldType,
                    actualFieldTypes[expectedField.fieldName],
                    declaringType.Name + "." + expectedField.fieldName + " must be typed " +
                    expectedField.fieldType.Name + ".");
            }
        }

        /// <summary>
        /// The generic types through which blob data is addressed indirectly. A field typed with
        /// any of these hides its real payload in a generic argument, so both discovery and the
        /// texture scan must unwrap them before judging what a blob actually stores.
        /// </summary>
        private static readonly Type[] BlobIndirectionDefinitions =
        {
            typeof(BlobArray<>),
            typeof(BlobPtr<>),
            typeof(BlobAssetReference<>)
        };

        private static bool TryUnwrapBlobIndirection(Type candidateType, out Type payloadType)
        {
            payloadType = null;
            if (!candidateType.IsGenericType)
            {
                return false;
            }
            Type genericDefinition = candidateType.GetGenericTypeDefinition();
            foreach (Type indirectionDefinition in BlobIndirectionDefinitions)
            {
                if (genericDefinition == indirectionDefinition)
                {
                    payloadType = candidateType.GetGenericArguments()[0];
                    return true;
                }
            }
            return false;
        }

        private static Type UnwrapBlobIndirections(Type candidateType)
        {
            Type unwrappedType = candidateType;
            while (TryUnwrapBlobIndirection(unwrappedType, out Type payloadType))
            {
                unwrappedType = payloadType;
            }
            return unwrappedType;
        }

        /// <summary>
        /// Discovers, by reflection over the Runtime assembly, every type that can end up stored
        /// inside a blob asset — rather than trusting a hand-maintained list that a later build
        /// step would silently outgrow. Seeds are the generic arguments of every
        /// <c>BlobAssetReference</c> / <c>BlobArray</c> / <c>BlobPtr</c> field declared by any
        /// Runtime value type; the field graph is then walked transitively, unwrapping nested blob
        /// indirections, so a struct only reachable through several array hops is still reached.
        /// </summary>
        private static List<Type> DiscoverBlobReachableTypes()
        {
            Queue<Type> pendingTypes = new Queue<Type>();
            Type[] runtimeTypes = typeof(ClipRegistryBlob).Assembly.GetTypes();
            foreach (Type runtimeType in runtimeTypes)
            {
                if (!runtimeType.IsValueType)
                {
                    continue;
                }
                FieldInfo[] runtimeFields = runtimeType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo runtimeField in runtimeFields)
                {
                    if (TryUnwrapBlobIndirection(runtimeField.FieldType, out Type seedType))
                    {
                        pendingTypes.Enqueue(seedType);
                    }
                }
            }

            HashSet<Type> visitedTypes = new HashSet<Type>();
            List<Type> reachableTypes = new List<Type>();
            while (pendingTypes.Count > 0)
            {
                Type currentType = UnwrapBlobIndirections(pendingTypes.Dequeue());
                // Primitives, enums, and reference types carry no further struct layout to walk.
                // A reference type reached from a blob is not dropped silently: the scan below
                // reports it against the field of the struct that declares it.
                if (!currentType.IsValueType || currentType.IsPrimitive || currentType.IsEnum)
                {
                    continue;
                }
                if (!visitedTypes.Add(currentType))
                {
                    continue;
                }
                reachableTypes.Add(currentType);
                FieldInfo[] currentFields = currentType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo currentField in currentFields)
                {
                    pendingTypes.Enqueue(currentField.FieldType);
                }
            }
            return reachableTypes;
        }

        // -----------------------------------------------------------------------------------
        // Section 4.2 — blob structs.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ClipRegistryBlob_MatchesTheSection42Sketch()
        {
            AssertFieldsMatch(typeof(ClipRegistryBlob), new FieldContract[]
            {
                Field("schemaVersion", typeof(int)),
                Field("setKey", typeof(ulong)),
                Field("vatSetKey", typeof(ulong)),
                Field("layerCount", typeof(byte)),
                Field("sortedClipIds", typeof(BlobArray<ulong>)),
                Field("clips", typeof(BlobArray<ClipBlob>)),
                Field("sortedTargetIds", typeof(BlobArray<uint>)),
                Field("targetBoundsExtents", typeof(BlobArray<float3>)),
                Field("targetFramesPerVariant", typeof(BlobArray<int>)),
                Field("vatInfo", typeof(VatTextureInfoBlob))
            });
        }

        [Test]
        public void ClipBlob_MatchesTheSection42Sketch()
        {
            AssertFieldsMatch(typeof(ClipBlob), new FieldContract[]
            {
                Field("clipId", typeof(ulong)),
                Field("debugName", typeof(FixedString64Bytes)),
                Field("duration", typeof(float)),
                Field("defaultLoop", typeof(LoopMode)),
                Field("defaultBlendIn", typeof(float)),
                Field("defaultBlendOut", typeof(float)),
                Field("transformTracks", typeof(BlobArray<TransformTrackBlob>)),
                Field("spriteTracks", typeof(BlobArray<SpriteTrackBlob>)),
                Field("events", typeof(BlobArray<EventMarkerBlob>)),
                Field("vatFrameStart", typeof(int)),
                Field("vatFrameCount", typeof(int)),
                Field("vatFps", typeof(float)),
                Field("vatTargetRanges", typeof(BlobArray<VatTrackRangeBlob>)),
                Field("billboardTracks", typeof(BlobArray<BillboardTrackBlob>)),
                Field("offsetBounds", typeof(AABB))
            });
        }

        [Test]
        public void TrackAndKeyBlobs_MatchTheSection42Sketches()
        {
            AssertFieldsMatch(typeof(TransformTrackBlob), new FieldContract[]
            {
                Field("targetIndex", typeof(int)),
                Field("blendOp", typeof(TrackBlendOp)),
                Field("channels", typeof(AnimatedChannels)),
                Field("keys", typeof(BlobArray<TransformKeyBlob>))
            });

            AssertFieldsMatch(typeof(BillboardTrackBlob), new FieldContract[]
            {
                Field("rootId", typeof(uint)),
                Field("keys", typeof(BlobArray<BillboardKeyBlob>))
            });

            AssertFieldsMatch(typeof(BillboardKeyBlob), new FieldContract[]
            {
                Field("normalizedTime", typeof(float)),
                Field("angleOffsetRadians", typeof(float)),
                Field("blendWeight", typeof(float)),
                Field("enabled", typeof(bool)),
                Field("interpolation", typeof(Interpolation)),
                Field("bezierStartHandle", typeof(float2)),
                Field("bezierEndHandle", typeof(float2))
            });

            AssertFieldsMatch(typeof(TransformKeyBlob), new FieldContract[]
            {
                Field("normalizedTime", typeof(float)),
                Field("position", typeof(float3)),
                Field("rotation", typeof(float3)),
                Field("scale", typeof(float3)),
                Field("interpolation", typeof(Interpolation)),
                Field("bezierStartHandle", typeof(float2)),
                Field("bezierEndHandle", typeof(float2))
            });

            AssertFieldsMatch(typeof(SpriteTrackBlob), new FieldContract[]
            {
                Field("targetIndex", typeof(int)),
                Field("mode", typeof(SpriteFrameMode)),
                Field("sliceSpace", typeof(SpriteSliceSpace)),
                Field("baseIndex", typeof(int)),
                Field("keys", typeof(BlobArray<SpriteKeyBlob>))
            });

            AssertFieldsMatch(typeof(SpriteKeyBlob), new FieldContract[]
            {
                Field("normalizedTime", typeof(float)),
                Field("sliceIndex", typeof(int)),
                Field("indexMode", typeof(SpriteIndexMode)),
                Field("atlasRect", typeof(float4))
            });

            AssertFieldsMatch(typeof(EventMarkerBlob), new FieldContract[]
            {
                Field("normalizedTime", typeof(float)),
                Field("eventKey", typeof(uint)),
                Field("intParam", typeof(int)),
                Field("floatParam", typeof(float)),
                Field("windowSeconds", typeof(float))
            });
        }

        [Test]
        public void VatTextureInfoBlobAndBounds_MatchTheSection42Sketches()
        {
            AssertFieldsMatch(typeof(VatTextureInfoBlob), new FieldContract[]
            {
                Field("flavor", typeof(VatFlavor)),
                Field("textureWidth", typeof(int)),
                Field("rowsPerFrame", typeof(int)),
                Field("boneOrVertexCount", typeof(int))
            });

            // The bounds payload is Unity's own AABB (Unity.Mathematics.Extensions) rather than a
            // package-local struct, so that section 5.9's RenderBoundsUpdateSystem can assign it
            // straight into RenderBounds.Value. Its centre/half-extent shape is asserted here
            // because the section 4.5 bake hash and the section 4.6 bounds union both depend on it.
            AssertFieldsMatch(typeof(AABB), new FieldContract[]
            {
                Field("Center", typeof(float3)),
                Field("Extents", typeof(float3))
            });
        }

        [Test]
        public void BlobStructs_NeverStoreTextureOrManagedObjectReferences()
        {
            List<Type> blobReachableTypes = DiscoverBlobReachableTypes();

            // Guard against a vacuous pass: if discovery ever returns nothing, the scan below has
            // no work to do and would report success while checking zero fields.
            CollectionAssert.Contains(
                blobReachableTypes,
                typeof(ClipRegistryBlob),
                "Blob discovery must reach the registry root, or this scan proves nothing.");
            CollectionAssert.Contains(
                blobReachableTypes,
                typeof(ClipBlob),
                "Blob discovery must walk through BlobArray indirections into ClipBlob.");

            List<string> violations = new List<string>();
            foreach (Type blobStructType in blobReachableTypes)
            {
                FieldInfo[] blobFields = blobStructType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo blobField in blobFields)
                {
                    // Unwrap BlobArray/BlobPtr/BlobAssetReference so a payload smuggled inside an
                    // indirection — BlobArray<UnityObjectRef<Texture2D>>, say — is judged on what it
                    // actually stores rather than on the wrapper's own type.
                    Type blobFieldType = UnwrapBlobIndirections(blobField.FieldType);
                    if (typeof(UnityEngine.Object).IsAssignableFrom(blobFieldType))
                    {
                        violations.Add(blobStructType.Name + "." + blobField.Name + " (UnityEngine.Object)");
                    }
                    if (blobFieldType.IsGenericType &&
                        blobFieldType.GetGenericTypeDefinition() == typeof(UnityObjectRef<>))
                    {
                        violations.Add(blobStructType.Name + "." + blobField.Name + " (UnityObjectRef)");
                    }
                }
            }
            Assert.IsEmpty(
                violations,
                "Architecture section 4.2 is a hard constraint: blobs carry only metadata and keys, " +
                "never textures or other Unity objects. Offenders: " + string.Join(", ", violations));
        }

        // -----------------------------------------------------------------------------------
        // Section 5.2 — runtime components, buffers, and singletons.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ActorRootComponents_MatchTheSection52Inventory()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(ClipRegistry)));
            AssertFieldsMatch(typeof(ClipRegistry), new FieldContract[]
            {
                Field("Value", typeof(BlobAssetReference<ClipRegistryBlob>))
            });

            Assert.IsTrue(typeof(IBufferElementData).IsAssignableFrom(typeof(PlaybackLayer)));
            AssertFieldsMatch(typeof(PlaybackLayer), new FieldContract[]
            {
                Field("clip", typeof(ClipId)),
                Field("clipIndex", typeof(int)),
                Field("time", typeof(float)),
                Field("advanceStartTime", typeof(float)),
                Field("speed", typeof(float)),
                Field("loop", typeof(LoopMode)),
                Field("previousClip", typeof(ClipId)),
                Field("previousClipIndex", typeof(int)),
                Field("previousTime", typeof(float)),
                Field("previousSpeed", typeof(float)),
                Field("previousLoop", typeof(LoopMode)),
                Field("blendElapsed", typeof(float)),
                Field("blendDuration", typeof(float)),
                Field("queuedClip", typeof(ClipId)),
                Field("queuedSpeed", typeof(float)),
                Field("queuedLoop", typeof(LoopMode)),
                Field("queuedBlend", typeof(float)),
                Field("flags", typeof(PlaybackFlags))
            });

            Assert.IsTrue(typeof(IBufferElementData).IsAssignableFrom(typeof(AnimationCommand)));
            AssertFieldsMatch(typeof(AnimationCommand), new FieldContract[]
            {
                Field("kind", typeof(CommandKind)),
                Field("layerIndex", typeof(byte)),
                Field("clip", typeof(ClipId)),
                Field("speed", typeof(float)),
                Field("loop", typeof(LoopMode)),
                Field("blendDuration", typeof(float)),
                Field("time", typeof(float))
            });

            Assert.IsTrue(typeof(IBufferElementData).IsAssignableFrom(typeof(AnimEventOutput)));
            AssertFieldsMatch(typeof(AnimEventOutput), new FieldContract[]
            {
                Field("eventKey", typeof(uint)),
                Field("layerIndex", typeof(byte)),
                Field("clip", typeof(ClipId)),
                Field("intParam", typeof(int)),
                Field("floatParam", typeof(float))
            });

            // The one enableable component in the package that carries data rather than being a
            // bare tag: the bit set is the payload, and the enabled flag is the "any bit at all"
            // fast path in front of it.
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(AnimEventMask)));
            Assert.IsTrue(typeof(IEnableableComponent).IsAssignableFrom(typeof(AnimEventMask)));
            AssertFieldsMatch(typeof(AnimEventMask), new FieldContract[]
            {
                Field("bits", typeof(ulong))
            });

            Assert.IsTrue(typeof(IBufferElementData).IsAssignableFrom(typeof(RigPartRef)));
            AssertFieldsMatch(typeof(RigPartRef), new FieldContract[]
            {
                Field("part", typeof(Entity)),
                Field("targetIndex", typeof(int))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(SampleSettings)));
            AssertFieldsMatch(typeof(SampleSettings), new FieldContract[]
            {
                Field("rateHz", typeof(float)),
                Field("phase01", typeof(float))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(AnimLod)));
            AssertFieldsMatch(typeof(AnimLod), new FieldContract[]
            {
                Field("level", typeof(byte))
            });

            // Amendment A34: the presentation half's memory of which clips it last sampled, which
            // is the only thing in the archetype that can answer LOD 3's "unless the clip changes".
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(AnimSampleState)));
            AssertFieldsMatch(typeof(AnimSampleState), new FieldContract[]
            {
                Field("sampledClipSignature", typeof(int))
            });

            // Amendment A13: the actor-space rest bounds the entity baker supplies, which section
            // 5.8 unions with each clip's offset-space offsetBounds. Typed AABB so the bounds system
            // can hand it to RenderBounds.Value without a conversion.
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(ActorRestBounds)));
            AssertFieldsMatch(typeof(ActorRestBounds), new FieldContract[]
            {
                Field("value", typeof(AABB))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(VatTextureBinding)));
            AssertFieldsMatch(typeof(VatTextureBinding), new FieldContract[]
            {
                Field("setKey", typeof(ulong)),
                Field("boneOrPositionTexture", typeof(UnityObjectRef<Texture2D>)),
                Field("normalTexture", typeof(UnityObjectRef<Texture2D>))
            });
        }

        [Test]
        public void PartComponents_MatchTheSection52Inventory()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(RigPartBinding)));
            AssertFieldsMatch(typeof(RigPartBinding), new FieldContract[]
            {
                Field("actorRoot", typeof(Entity)),
                Field("targetIndex", typeof(int))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(TargetRestPose)));
            AssertFieldsMatch(typeof(TargetRestPose), new FieldContract[]
            {
                Field("localPosition", typeof(float3)),
                Field("rotation", typeof(float3)),
                Field("scale", typeof(float3)),
                Field("restSliceIndex", typeof(int))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(TargetPose)));
            AssertFieldsMatch(typeof(TargetPose), new FieldContract[]
            {
                Field("localPosition", typeof(float3)),
                Field("rotation", typeof(float3)),
                Field("scale", typeof(float3)),
                Field("sliceIndex", typeof(int)),
                Field("atlasRect", typeof(float4))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(VatDriven)));
            AssertFieldsMatch(typeof(VatDriven), new FieldContract[]
            {
                Field("layerIndex", typeof(byte))
            });
        }

        [Test]
        public void WorldSingletons_MatchTheSection52Inventory()
        {
            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(AnimationToolkitConfig)));
            AssertFieldsMatch(typeof(AnimationToolkitConfig), new FieldContract[]
            {
                Field("defaultSampleRateHz", typeof(float)),
                Field("distanceLodEnabled", typeof(bool)),
                Field("lodDistancesSq", typeof(float4))
            });

            Assert.IsTrue(typeof(IComponentData).IsAssignableFrom(typeof(AnimationToolkitCameraData)));
            AssertFieldsMatch(typeof(AnimationToolkitCameraData), new FieldContract[]
            {
                Field("position", typeof(float3)),

                // Amendment A39: screen-aligned billboarding needs the camera's forward, and it must
                // be a host-written per-frame value for the same reason position is — it has to be
                // identical in the ShadowCaster pass, where UNITY_MATRIX_V is the light's.
                Field("forward", typeof(float3))
            });
        }

        [Test]
        public void EnableableTags_CarryNoFields()
        {
            Type[] enableableTagTypes =
            {
                typeof(AnimationCommandPending),
                typeof(AnimEventsPending),
                typeof(RigBindingUninitialized),
                typeof(AnimVisible),
                typeof(BoundsDirty)
            };
            foreach (Type enableableTagType in enableableTagTypes)
            {
                Assert.IsTrue(
                    typeof(IComponentData).IsAssignableFrom(enableableTagType),
                    enableableTagType.Name + " must be an IComponentData.");
                AssertFieldsMatch(enableableTagType, new FieldContract[0]);
            }
        }
    }
}
