// Copyright (c) 2026 Stitch Punk. All rights reserved.

using StitchPunk.AnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.AnimationToolkitMigration
{
    /// <summary>
    /// Ticks every <see cref="PilotDriver"/>, issuing crossfades and toggling facing so the §13.2
    /// step-2 review can watch behaviours a static scene cannot show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs <em>before</em> the toolkit group so a command issued this frame is applied by
    /// <c>CommandApplySystem</c> in the same frame rather than the next — otherwise every swap would
    /// carry a one-frame lag that looks like a stutter and would be blamed on the blend.
    /// </para>
    /// <para>
    /// This is review scaffolding, not migration output. The shipped host drives playback from its
    /// behaviour systems and facing from movement; delete this once the call-site rewrites land.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationToolkitSystemGroup))]
    [BurstCompile]
    public partial struct PilotDriverSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PilotDriver>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DrivePilotJob driveJob = new DrivePilotJob
            {
                deltaTime = SystemAPI.Time.DeltaTime,
                partFacingLookup = SystemAPI.GetComponentLookup<PartFacing>()
            };
            state.Dependency = driveJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Advances one pilot actor's three independent timers and applies whichever fired.
    /// </summary>
    /// <remarks>
    /// <see cref="AnimationCommandPending"/> is declared <c>WithPresent</c> because this job turns
    /// the bit <em>on</em>. An <c>EnabledRefRW&lt;T&gt;</c> parameter enrols <c>T</c> as an enabled-
    /// filtered <c>All</c> component by default, which would restrict the job to actors that already
    /// had a command pending — so the very first command would never be issued, and nothing would
    /// ever play.
    /// </remarks>
    [BurstCompile]
    [WithPresent(typeof(AnimationCommandPending))]
    internal partial struct DrivePilotJob : IJobEntity
    {
        public float deltaTime;

        /// <summary>
        /// Written on part entities, never on the actor being iterated. Safe in parallel because a
        /// part belongs to exactly one actor's <see cref="RigPartRef"/> buffer.
        /// </summary>
        [NativeDisableParallelForRestriction] public ComponentLookup<PartFacing> partFacingLookup;

        private void Execute(
            ref PilotDriver driver,
            ref DynamicBuffer<AnimationCommand> commands,
            in DynamicBuffer<RigPartRef> partRefs,
            EnabledRefRW<AnimationCommandPending> animationCommandPendingEnabled)
        {
            driver.clipTimer += deltaTime;
            driver.mirrorTimer += deltaTime;
            driver.viewTimer += deltaTime;

            if (driver.clipInterval > 0f && driver.clipTimer >= driver.clipInterval)
            {
                driver.clipTimer = 0f;
                driver.showingSecondClip = !driver.showingSecondClip;

                ClipId nextClip = driver.showingSecondClip ? driver.secondClip : driver.firstClip;
                if (nextClip.IsValid)
                {
                    AnimationCommandUtil.Play(
                        ref commands,
                        animationCommandPendingEnabled,
                        driver.layerIndex,
                        nextClip,
                        1f,
                        LoopMode.Loop,
                        driver.blendDuration);
                }
            }

            if (driver.mirrorInterval > 0f && driver.mirrorTimer >= driver.mirrorInterval)
            {
                driver.mirrorTimer = 0f;
                driver.mirrored = !driver.mirrored;
                WriteFacingToParts(in partRefs, driver.viewOffset, driver.mirrored);
            }

            if (driver.viewInterval > 0f && driver.viewTimer >= driver.viewInterval)
            {
                driver.viewTimer = 0f;

                // Unbounded on purpose: ResolveViewSlice wraps inside the target's own variant block,
                // so a driver that counts up forever is a live check that the wrap really holds.
                driver.viewOffset++;
                WriteFacingToParts(in partRefs, driver.viewOffset, driver.mirrored);
            }
        }

        /// <summary>
        /// Pushes the actor-level facing state onto every part that opted in.
        /// </summary>
        /// <remarks>
        /// Parts that never opted in are skipped rather than added to — a driver has no business
        /// changing an actor's archetype, and the <c>HasComponent</c> check is what keeps the
        /// opt-in meaningful.
        /// </remarks>
        private void WriteFacingToParts(in DynamicBuffer<RigPartRef> partRefs, int viewOffset, bool mirrored)
        {
            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                Entity partEntity = partRefs[partRefIndex].part;
                if (!partFacingLookup.HasComponent(partEntity))
                {
                    continue;
                }
                partFacingLookup[partEntity] = new PartFacing
                {
                    viewOffset = viewOffset,
                    mirrorX = mirrored
                };
            }
        }
    }
}
