// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using StitchPunk.AnimationToolkit;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>SocketResolveSystem</c> — the socket-attachment placement system that shipped with
    /// no runtime coverage at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SocketRegistryBlob</c> has no authoring-side counterpart small enough to construct through
    /// a rig asset in a PlayMode test, so every fixture builds one by hand with a
    /// <see cref="BlobBuilder"/>, mirroring the field order and sort <c>SocketRegistryBuilder</c>
    /// produces: <c>sortedSocketIds</c>/<c>sockets</c> allocated and filled together in ascending id
    /// order, then <c>clipTracks</c> allocated and filled afterward.
    /// </para>
    /// <para>
    /// Actors and parts are built through <see cref="PlaybackTestActor"/> where it fits, exactly as
    /// <c>VatMaterialSystemTests</c> does. It does not fully fit for rotated parts: <see
    /// cref="PlaybackTestActor.AddPart"/> seeds a part's <c>LocalTransform</c> with
    /// <c>LocalTransform.FromPosition</c> — position only, because in production a part's rotation is
    /// written later by <c>TransformApplySystem</c>, which these fixtures never run. Fixtures that
    /// need a rotated part overwrite <c>LocalTransform</c> after <c>AddPart</c> to supply one.
    /// </para>
    /// <para>
    /// Every happy-path attachment starts from <see cref="SentinelTransform"/> — values no resolve
    /// path would ever produce — so a fixture asserting the expected pose is asserting that a write
    /// happened, the same reasoning <c>VatMaterialSystemTests.ScribbleProperties</c> uses. The two
    /// "leaves it alone" fixtures assert equality with that same sentinel instead.
    /// </para>
    /// </remarks>
    public sealed class SocketResolveSystemTests
    {
        private const float Tolerance = 1e-4f;

        private const uint RigTargetSocketId = 100;
        private const uint SecondRigTargetSocketId = 101;
        private const uint UnknownSocketId = 999;
        private const uint BoneSocketId = 200;
        private const ulong BoneClipId = 700;
        private const ulong SwingClipId = 800;

        private World testWorld;
        private double elapsedFrameTime;
        private readonly List<BlobAssetReference<ClipRegistryBlob>> clipRegistriesToDispose =
            new List<BlobAssetReference<ClipRegistryBlob>>();
        private readonly List<BlobAssetReference<SocketRegistryBlob>> socketRegistriesToDispose =
            new List<BlobAssetReference<SocketRegistryBlob>>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SocketResolveSystemTests");
            elapsedFrameTime = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            for (int registryIndex = 0; registryIndex < clipRegistriesToDispose.Count; registryIndex++)
            {
                if (clipRegistriesToDispose[registryIndex].IsCreated)
                {
                    clipRegistriesToDispose[registryIndex].Dispose();
                }
            }
            clipRegistriesToDispose.Clear();

            for (int registryIndex = 0; registryIndex < socketRegistriesToDispose.Count; registryIndex++)
            {
                if (socketRegistriesToDispose[registryIndex].IsCreated)
                {
                    socketRegistriesToDispose[registryIndex].Dispose();
                }
            }
            socketRegistriesToDispose.Clear();
        }

        /// <summary>
        /// Catches: composing a rig-target socket's world pose without rotating the offset by the
        /// part's own rotation — e.g. adding <c>socket.localPosition + attachment.localOffset</c>
        /// straight onto the part's position. The part sits away from the origin <em>and</em> is
        /// rotated 90 degrees, so an unrotated offset lands at a different point than a correctly
        /// rotated one, and the two cannot be confused by coincidence.
        /// </summary>
        [Test]
        public void ARigTargetSocket_LandsOnThePartsPoseOffsetInSocketSpace()
        {
            Entity actor = BuildActor();

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: new float3(1f, 0f, 0f))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            Entity part = PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 0, restPosition: new float3(2f, 3f, 0f));
            testWorld.EntityManager.SetComponentData(
                part,
                LocalTransform.FromPositionRotationScale(
                    new float3(2f, 3f, 0f), quaternion.RotateZ(math.radians(90f)), 1f));

            Entity attachment = CreateAttachment(actor, RigTargetSocketId, new float3(0f, 1f, 0f), SentinelTransform());

            RunSocketResolveSystem();

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);
            Assert.AreEqual(1f, result.Position.x, Tolerance,
                "Socket local (1,0,0) plus extra offset (0,1,0) rotated 90 degrees with the part must land at x=1, not x=3.");
            Assert.AreEqual(4f, result.Position.y, Tolerance,
                "The same rotated offset puts y at 4; an unrotated offset would give y=4 too by coincidence on this axis alone, which is why x is the discriminating assertion.");
            Assert.AreEqual(0f, result.Position.z, Tolerance);
            Assert.AreEqual(math.radians(90f), ExtractRotationZ(result.Rotation), Tolerance,
                "The attachment must inherit the part's rotation.");
            Assert.AreEqual(1f, result.Scale, Tolerance);
        }

        /// <summary>
        /// Catches: applying <c>attachment.localOffset</c> in world (or actor) space instead of
        /// socket space. Two sockets share the same offset and sit on parts at the same position, one
        /// unrotated and one rotated 90 degrees; a world-space offset would place both attachments
        /// identically because it never reads either part's rotation, while a correct, socket-space
        /// offset must swing from +X to +Y along with the rotated part.
        /// </summary>
        [Test]
        public void TheOffset_RotatesWithThePart()
        {
            Entity actor = BuildActor();

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: new float3(1f, 0f, 0f)),
                RigTargetSocket(SecondRigTargetSocketId, targetIndex: 1, localPosition: new float3(1f, 0f, 0f))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            // Unrotated part: LocalTransform.FromPosition already seeds identity rotation, so no
            // override is needed to get the "rotation = 0" half of the comparison.
            PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 0, restPosition: float3.zero);

            Entity rotatedPart = PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 1, restPosition: float3.zero);
            testWorld.EntityManager.SetComponentData(
                rotatedPart,
                LocalTransform.FromPositionRotationScale(float3.zero, quaternion.RotateZ(math.radians(90f)), 1f));

            Entity attachmentOnUnrotatedPart =
                CreateAttachment(actor, RigTargetSocketId, new float3(1f, 0f, 0f), SentinelTransform());
            Entity attachmentOnRotatedPart =
                CreateAttachment(actor, SecondRigTargetSocketId, new float3(1f, 0f, 0f), SentinelTransform());

            RunSocketResolveSystem();

            LocalTransform unrotatedResult = testWorld.EntityManager.GetComponentData<LocalTransform>(attachmentOnUnrotatedPart);
            LocalTransform rotatedResult = testWorld.EntityManager.GetComponentData<LocalTransform>(attachmentOnRotatedPart);

            Assert.AreEqual(2f, unrotatedResult.Position.x, Tolerance, "Baseline: the 2-unit offset sits along +X on the unrotated part.");
            Assert.AreEqual(0f, unrotatedResult.Position.y, Tolerance);

            Assert.AreEqual(0f, rotatedResult.Position.x, Tolerance,
                "A world-space offset would still read 2 here, unchanged by the part's rotation.");
            Assert.AreEqual(2f, rotatedResult.Position.y, Tolerance,
                "The same offset must swing onto +Y once the part it rides is rotated 90 degrees.");
        }

        /// <summary>
        /// Catches: reading a part's <c>LocalToWorld</c> for its world pose instead of composing the
        /// actor's <c>LocalToWorld</c> with the part's freshly written <c>LocalTransform</c>. The
        /// remarks on <c>SocketResolveSystem</c> call this out explicitly — Unity's transform systems
        /// run after this group, so a part's own <c>LocalToWorld</c> is still last frame's. The part
        /// here carries a deliberately wrong, stale <c>LocalToWorld</c> alongside a correct, fresh
        /// <c>LocalTransform</c>; only composing the two derives the right answer.
        /// </summary>
        [Test]
        public void TheWorldTransform_IsComposedFromTheActorMatrixAndThePartsFreshLocalTransform()
        {
            Entity actor = BuildActor();
            testWorld.EntityManager.SetComponentData(
                actor,
                new LocalToWorld
                {
                    Value = float4x4.TRS(
                        new float3(5f, 0f, 0f), quaternion.RotateZ(math.radians(90f)), new float3(1f, 1f, 1f))
                });

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: float3.zero)
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            // AddPart's LocalTransform (position only, identity rotation) is exactly this frame's
            // fresh local sample; no override needed.
            Entity part = PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 0, restPosition: new float3(2f, 0f, 0f));

            // A stale world matrix the transform systems have not refreshed yet this frame. A correct
            // implementation never reads this component.
            testWorld.EntityManager.AddComponentData(
                part,
                new LocalToWorld
                {
                    Value = float4x4.TRS(new float3(999f, 999f, 0f), quaternion.identity, new float3(1f, 1f, 1f))
                });

            Entity attachment = CreateAttachment(actor, RigTargetSocketId, float3.zero, SentinelTransform());

            RunSocketResolveSystem();

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);
            Assert.AreEqual(5f, result.Position.x, Tolerance,
                "Composing actor LocalToWorld (translate 5,0,0 / rotate 90) with the part's fresh local (2,0,0) gives (5,2,0).");
            Assert.AreEqual(2f, result.Position.y, Tolerance);
            Assert.AreEqual(0f, result.Position.z, Tolerance,
                "Reading the part's stale LocalToWorld (999,999,0) instead would land nowhere near here.");
            Assert.AreEqual(math.radians(90f), ExtractRotationZ(result.Rotation), Tolerance,
                "The actor's rotation must reach the attachment too, confirming composition rather than a stale read.");
        }

        /// <summary>
        /// Catches: falling through to a default/zeroed pose when <see cref="SocketAttachment.socketId"/>
        /// names no socket in the registry, which would silently teleport the attachment to the
        /// origin. The correct behaviour is to leave <c>LocalTransform</c> exactly as it was.
        /// </summary>
        [Test]
        public void AnUnknownSocketId_LeavesTheAttachmentWhereItWas()
        {
            Entity actor = BuildActor();

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: float3.zero)
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            LocalTransform sentinel = SentinelTransform();
            Entity attachment = CreateAttachment(actor, UnknownSocketId, new float3(3f, 3f, 3f), sentinel);

            RunSocketResolveSystem();

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);
            Assert.AreEqual(sentinel.Position.x, result.Position.x, Tolerance,
                "An id the registry does not know must not move the attachment at all.");
            Assert.AreEqual(sentinel.Position.y, result.Position.y, Tolerance);
            Assert.AreEqual(sentinel.Position.z, result.Position.z, Tolerance,
                "In particular it must not snap to the origin.");
            Assert.AreEqual(ExtractRotationZ(sentinel.Rotation), ExtractRotationZ(result.Rotation), Tolerance);
            Assert.AreEqual(sentinel.Scale, result.Scale, Tolerance);
        }

        /// <summary>
        /// Catches: snapping a bone socket to the nearer baked sample instead of interpolating
        /// between the two the playback time falls between. Two samples are baked one second apart at
        /// fps = 1, sampled at exactly the midpoint, so a snapping implementation reads one whole
        /// sample away from the correct answer in both position and rotation.
        /// </summary>
        [Test]
        public void ABoneSocket_InterpolatesBetweenBakedSamples()
        {
            Entity actor = BuildActorWithClip(BoneClipId, duration: 1f);

            SocketSpec[] socketSpecs = { BoneSocket(BoneSocketId, layerIndex: 0) };
            ClipTrackSpec[] trackSpecs =
            {
                Track(
                    BoneClipId,
                    socketIndex: 0,
                    fps: 1f,
                    Sample(float3.zero, quaternion.identity),
                    Sample(new float3(10f, 4f, 0f), quaternion.RotateZ(math.radians(90f))))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs, trackSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(BoneClipId);
            layer.clipIndex = 0;
            layer.time = 0.5f;
            layer.advanceStartTime = 0.5f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            Entity attachment = CreateAttachment(actor, BoneSocketId, float3.zero, SentinelTransform());

            RunSocketResolveSystem();

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);
            Assert.AreEqual(5f, result.Position.x, Tolerance,
                "Halfway between (0,0,0) and (10,4,0) is (5,2,0); a snapping read would give one or the other.");
            Assert.AreEqual(2f, result.Position.y, Tolerance);
            Assert.AreEqual(0f, result.Position.z, Tolerance);
            Assert.AreEqual(math.radians(45f), ExtractRotationZ(result.Rotation), Tolerance,
                "Slerping identity toward a 90-degree sample at the midpoint gives 45 degrees, not 0 or 90.");
        }

        /// <summary>
        /// Catches: sampling a bone socket's track regardless of whether the driving layer is
        /// actually playing. A stopped layer still carries its last clip index and time (the same
        /// state <c>VatMaterialSystemTests.AStoppedLayer_LeavesTheFrameWhereItWas</c> exercises for
        /// the VAT path), so without the active-flag check the attachment would keep being resolved
        /// from state nothing is advancing. <c>LocalTransform</c> must be left untouched.
        /// </summary>
        [Test]
        public void ABoneSocketWithAnInactiveDrivingLayer_ResolvesNothing()
        {
            Entity actor = BuildActorWithClip(BoneClipId, duration: 1f);

            SocketSpec[] socketSpecs = { BoneSocket(BoneSocketId, layerIndex: 0) };
            ClipTrackSpec[] trackSpecs =
            {
                Track(
                    BoneClipId,
                    socketIndex: 0,
                    fps: 1f,
                    Sample(float3.zero, quaternion.identity),
                    Sample(new float3(10f, 4f, 0f), quaternion.RotateZ(math.radians(90f))))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs, trackSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(BoneClipId);
            layer.clipIndex = 0;
            layer.time = 0.5f;
            layer.advanceStartTime = 0.5f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.None;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            LocalTransform sentinel = SentinelTransform();
            Entity attachment = CreateAttachment(actor, BoneSocketId, float3.zero, sentinel);

            RunSocketResolveSystem();

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);
            Assert.AreEqual(sentinel.Position.x, result.Position.x, Tolerance,
                "An inactive driving layer must leave the attachment exactly where it was.");
            Assert.AreEqual(sentinel.Position.y, result.Position.y, Tolerance);
            Assert.AreEqual(sentinel.Position.z, result.Position.z, Tolerance);
            Assert.AreEqual(ExtractRotationZ(sentinel.Rotation), ExtractRotationZ(result.Rotation), Tolerance);
            Assert.AreEqual(sentinel.Scale, result.Scale, Tolerance);
        }

        // -------------------------------------------------------------------------------------
        // Full-pipeline integration
        //
        // Every fixture above runs SocketResolveSystem alone against a hand-seeded part, which is
        // what lets them isolate its composition maths — and is also why none of them can say
        // whether the pose it composes from is the one the rest of the toolkit actually produces.
        // The two fixtures below drive a socket through a real frame instead: a Play command, the
        // clip sampled, TransformApplySystem writing the part's LocalTransform, and only then the
        // socket resolved from it.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// <strong>The chain the package exists to keep aligned</strong>: clip → sampled pose →
        /// applied transform → socket. Catches any break between them, and specifically a
        /// <c>SocketResolveSystem</c> that reads a part's <em>rest</em> pose rather than its
        /// animated one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The clip animates <strong>rotation only</strong>, and the part rests at the origin. That
        /// makes the part's rotation the sole thing that can move the attachment: a socket one unit
        /// out along +X swings to +Y as the part turns 90°, while a resolve that used the rest
        /// rotation — or ran before <c>TransformApplySystem</c> wrote the animated one — would leave
        /// the attachment sitting at (1, 0, 0) for the whole clip. Animating position instead would
        /// have moved the attachment under either behaviour and proved nothing.
        /// </para>
        /// <para>
        /// The expected positions are computed from the clip's authored angles rather than read back
        /// from the part, so the assertion is not satisfied by the two systems agreeing on a wrong
        /// value.
        /// </para>
        /// </remarks>
        [Test]
        public void ASocketOnAnAnimatedPart_FollowsThePoseTheFrameActuallyApplied()
        {
            Entity actor = BuildActorWithSwingClip();
            Entity part = PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 0);

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: new float3(1f, 0f, 0f))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            Entity attachment =
                CreateAttachment(actor, RigTargetSocketId, float3.zero, SentinelTransform());

            PlaybackTestActor.EnqueueCommand(
                testWorld, actor, PlaybackTestActor.PlayCommand(0, SwingClipId));

            // Half the clip: the part has turned 45 degrees, so the socket sits on the diagonal.
            RunAnimationFrame(0.5f);

            float halfway = math.sqrt(0.5f);
            LocalTransform atHalfway =
                testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);

            Assert.AreEqual(halfway, atHalfway.Position.x, Tolerance,
                "The socket did not follow the part's animated rotation. At x=1 it never rotated at "
                + "all; SocketResolveSystem is reading a pose the frame did not apply.");
            Assert.AreEqual(halfway, atHalfway.Position.y, Tolerance);
            Assert.AreEqual(math.radians(45f), ExtractRotationZ(atHalfway.Rotation), Tolerance,
                "The attachment must inherit the part's animated rotation, not its rest rotation.");

            // Three quarters through: 67.5 degrees, and still mid-clip.
            //
            // Deliberately not the clip's final frame. A Once clip goes inactive the instant it
            // completes, so TransformSampleSystem stops compositing it and the part returns to its
            // rest pose — which would put this socket back at exactly (1, 0, 0), the same reading a
            // socket that never tracked anything gives. Sampling the end would therefore assert the
            // one value that cannot tell the two apart.
            RunAnimationFrame(0.25f);

            float threeQuarterAngle = math.radians(67.5f);
            LocalTransform laterInTheClip =
                testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);

            Assert.AreEqual(math.cos(threeQuarterAngle), laterInTheClip.Position.x, Tolerance,
                "The socket must keep tracking the part across frames, not resolve once and stick.");
            Assert.AreEqual(math.sin(threeQuarterAngle), laterInTheClip.Position.y, Tolerance);
        }

        /// <summary>
        /// Catches: <c>SocketResolveSystem</c> ordered before <c>TransformApplySystem</c> in the
        /// presentation group. The socket would then compose from the transform the *previous* frame
        /// applied, which is invisible on a still actor and a permanent one-frame lag on a moving
        /// one — precisely the class of drift a weapon held in a swinging hand shows as separation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asserting the lag directly rather than the ordering attribute: an attribute test says the
        /// edge is declared, this one says the edge produces same-frame data.
        /// </para>
        /// <para>
        /// <strong>The position assertion is the discriminating one, not the sentinel assertion.</strong>
        /// Verified by running the socket resolve ahead of the apply: the attachment does not keep
        /// its seed value, because the part still holds its <em>rest</em> transform at that point and
        /// the resolve composes perfectly well from it — landing at an unrotated (1, 0, 0). So a
        /// one-frame lag looks like a successful write of a plausible pose, and only the value says
        /// otherwise. The sentinel check is kept to separate "wrote the wrong pose" from "wrote
        /// nothing", which are different bugs with the same symptom on a still actor.
        /// </para>
        /// </remarks>
        [Test]
        public void TheSocketResolvesFromThisFramesTransform_NotThePreviousFrames()
        {
            Entity actor = BuildActorWithSwingClip();
            PlaybackTestActor.AddPart(testWorld, actor, targetIndex: 0);

            SocketSpec[] socketSpecs =
            {
                RigTargetSocket(RigTargetSocketId, targetIndex: 0, localPosition: new float3(1f, 0f, 0f))
            };
            BlobAssetReference<SocketRegistryBlob> socketRegistry = BuildSocketRegistry(socketSpecs);
            socketRegistriesToDispose.Add(socketRegistry);
            AttachSocketRegistry(actor, socketRegistry);

            LocalTransform sentinel = SentinelTransform();
            Entity attachment = CreateAttachment(actor, RigTargetSocketId, float3.zero, sentinel);

            PlaybackTestActor.EnqueueCommand(
                testWorld, actor, PlaybackTestActor.PlayCommand(0, SwingClipId));
            RunAnimationFrame(0.5f);

            LocalTransform result = testWorld.EntityManager.GetComponentData<LocalTransform>(attachment);

            Assert.AreNotEqual(sentinel.Position.x, result.Position.x,
                "The attachment still holds its seed value, so nothing resolved on the frame the "
                + "part was first animated.");
            Assert.AreEqual(math.sqrt(0.5f), result.Position.x, Tolerance,
                "The socket composed from a stale transform: this is the pose applied this frame.");
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// An actor whose registry holds one 1-second clip turning target 0 through 90° about Z.
        /// </summary>
        private Entity BuildActorWithSwingClip()
        {
            BlobAssetReference<ClipRegistryBlob> clipRegistry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = SwingClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Once,
                        transformTracks = new[]
                        {
                            new PlaybackTestActor.TransformTrackSpec
                            {
                                targetIndex = 0,
                                blendOp = TrackBlendOp.Override,
                                channels = AnimatedChannels.Rotation,
                                keys = new[]
                                {
                                    PlaybackTestActor.Key(0f, rotationZ: 0f),
                                    PlaybackTestActor.Key(1f, rotationZ: math.radians(90f))
                                }
                            }
                        }
                    }
                },
                targetCount: 1);
            clipRegistriesToDispose.Add(clipRegistry);
            return PlaybackTestActor.CreateActor(testWorld, clipRegistry);
        }

        /// <summary>
        /// Runs one whole frame in the group order <c>AnimationToolkitSystemGroup</c> declares, so a
        /// fixture reads what a real frame produced rather than what one system did in isolation.
        /// </summary>
        private void RunAnimationFrame(float deltaTime)
        {
            elapsedFrameTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedFrameTime, deltaTime));

            UpdateSystem<CommandApplySystem>();
            UpdateSystem<PlaybackTimeSystem>();
            UpdateSystem<TransformSampleSystem>();
            UpdateSystem<TransformApplySystem>();
            UpdateSystem<SocketResolveSystem>();

            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void UpdateSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
        }

        /// <summary>Values no resolve path would produce, so a matching assertion proves a write happened.</summary>
        private static LocalTransform SentinelTransform()
        {
            return LocalTransform.FromPositionRotationScale(
                new float3(-999f, -999f, -999f), quaternion.RotateZ(math.radians(-123f)), 7.5f);
        }

        /// <summary>Extracts the Z-axis rotation angle from a quaternion known to rotate about Z only.</summary>
        private static float ExtractRotationZ(quaternion rotation)
        {
            return math.atan2(
                2f * rotation.value.w * rotation.value.z,
                1f - 2f * rotation.value.z * rotation.value.z);
        }

        /// <summary>An actor with no clip registry content, for fixtures that only need rig-target sockets.</summary>
        private Entity BuildActor()
        {
            BlobAssetReference<ClipRegistryBlob> clipRegistry =
                PlaybackTestActor.BuildRegistry(Array.Empty<PlaybackTestActor.ClipSpec>());
            clipRegistriesToDispose.Add(clipRegistry);
            return PlaybackTestActor.CreateActor(testWorld, clipRegistry);
        }

        /// <summary>An actor whose clip registry carries one loopable clip, for bone-socket fixtures.</summary>
        private Entity BuildActorWithClip(ulong clipId, float duration)
        {
            BlobAssetReference<ClipRegistryBlob> clipRegistry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = clipId,
                        duration = duration,
                        defaultLoop = LoopMode.Loop
                    }
                });
            clipRegistriesToDispose.Add(clipRegistry);
            return PlaybackTestActor.CreateActor(testWorld, clipRegistry, layerCount: 2);
        }

        private void AttachSocketRegistry(Entity actorEntity, BlobAssetReference<SocketRegistryBlob> registry)
        {
            testWorld.EntityManager.AddComponentData(actorEntity, new SocketRegistry { Value = registry });
        }

        private Entity CreateAttachment(Entity actorRoot, uint socketId, float3 localOffset, LocalTransform seedTransform)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity attachmentEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(attachmentEntity, new SocketAttachment
            {
                actorRoot = actorRoot,
                socketId = socketId,
                localOffset = localOffset
            });
            entityManager.AddComponentData(attachmentEntity, seedTransform);
            return attachmentEntity;
        }

        private void RunSocketResolveSystem()
        {
            SystemHandle socketResolveSystem = testWorld.GetOrCreateSystem<SocketResolveSystem>();
            socketResolveSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // -------------------------------------------------------------------------------------
        // Socket registry blob construction
        //
        // Mirrors StitchPunk.AnimationToolkit.Authoring.SocketRegistryBuilder.TryBuild: sockets are
        // sorted ascending by id, sortedSocketIds and sockets are allocated and filled together in
        // that order, and clipTracks is allocated and filled afterward.
        // -------------------------------------------------------------------------------------

        private struct SocketSpec
        {
            internal uint socketId;
            internal SocketAttachMode mode;
            internal int targetIndex;
            internal byte layerIndex;
            internal float3 localPosition;
            internal quaternion localRotation;
        }

        private struct SocketSampleSpec
        {
            internal float3 position;
            internal quaternion rotation;
        }

        private struct ClipTrackSpec
        {
            internal ulong clipId;
            internal int socketIndex;
            internal float fps;
            internal SocketSampleSpec[] samples;
        }

        private static SocketSpec RigTargetSocket(uint socketId, int targetIndex, float3 localPosition)
        {
            return new SocketSpec
            {
                socketId = socketId,
                mode = SocketAttachMode.RigTarget,
                targetIndex = targetIndex,
                layerIndex = 0,
                localPosition = localPosition,
                localRotation = quaternion.identity
            };
        }

        private static SocketSpec BoneSocket(uint socketId, byte layerIndex)
        {
            return new SocketSpec
            {
                socketId = socketId,
                mode = SocketAttachMode.Bone,
                targetIndex = -1,
                layerIndex = layerIndex,
                localPosition = float3.zero,
                localRotation = quaternion.identity
            };
        }

        private static ClipTrackSpec Track(ulong clipId, int socketIndex, float fps, params SocketSampleSpec[] samples)
        {
            return new ClipTrackSpec { clipId = clipId, socketIndex = socketIndex, fps = fps, samples = samples };
        }

        private static SocketSampleSpec Sample(float3 position, quaternion rotation)
        {
            return new SocketSampleSpec { position = position, rotation = rotation };
        }

        private static BlobAssetReference<SocketRegistryBlob> BuildSocketRegistry(
            SocketSpec[] socketSpecs, ClipTrackSpec[] clipTrackSpecs = null)
        {
            SocketSpec[] canonicalSpecs = (SocketSpec[])socketSpecs.Clone();
            Array.Sort(canonicalSpecs, CompareSocketSpecsById);
            ClipTrackSpec[] trackSpecs = clipTrackSpecs ?? Array.Empty<ClipTrackSpec>();

            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref SocketRegistryBlob root = ref blobBuilder.ConstructRoot<SocketRegistryBlob>();
                root.schemaVersion = SocketRegistryBuilder.SchemaVersion;
                root.rigKey = 1UL;

                BlobBuilderArray<uint> idArray = blobBuilder.Allocate(ref root.sortedSocketIds, canonicalSpecs.Length);
                BlobBuilderArray<SocketDefinitionBlob> definitionArray =
                    blobBuilder.Allocate(ref root.sockets, canonicalSpecs.Length);
                for (int socketIndex = 0; socketIndex < canonicalSpecs.Length; socketIndex++)
                {
                    SocketSpec socketSpec = canonicalSpecs[socketIndex];
                    idArray[socketIndex] = socketSpec.socketId;
                    definitionArray[socketIndex] = new SocketDefinitionBlob
                    {
                        mode = socketSpec.mode,
                        targetIndex = socketSpec.targetIndex,
                        layerIndex = socketSpec.layerIndex,
                        localPosition = socketSpec.localPosition,
                        localRotation = socketSpec.localRotation
                    };
                }

                BlobBuilderArray<SocketClipTrackBlob> trackArray =
                    blobBuilder.Allocate(ref root.clipTracks, trackSpecs.Length);
                for (int trackIndex = 0; trackIndex < trackSpecs.Length; trackIndex++)
                {
                    ClipTrackSpec trackSpec = trackSpecs[trackIndex];
                    ref SocketClipTrackBlob trackBlob = ref trackArray[trackIndex];
                    trackBlob.clipId = trackSpec.clipId;
                    trackBlob.socketIndex = trackSpec.socketIndex;
                    trackBlob.fps = trackSpec.fps;

                    BlobBuilderArray<SocketSampleBlob> sampleArray =
                        blobBuilder.Allocate(ref trackBlob.samples, trackSpec.samples.Length);
                    for (int sampleIndex = 0; sampleIndex < trackSpec.samples.Length; sampleIndex++)
                    {
                        sampleArray[sampleIndex] = new SocketSampleBlob
                        {
                            position = trackSpec.samples[sampleIndex].position,
                            rotation = trackSpec.samples[sampleIndex].rotation
                        };
                    }
                }

                return blobBuilder.CreateBlobAssetReference<SocketRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                blobBuilder.Dispose();
            }
        }

        private static int CompareSocketSpecsById(SocketSpec first, SocketSpec second)
        {
            return first.socketId.CompareTo(second.socketId);
        }
    }
}
