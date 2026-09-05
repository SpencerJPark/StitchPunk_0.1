using System;
using System.Collections.Generic;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests
{
    // Makes the documented pipeline in SystemGroups.cs executable instead of aspirational.
    // Unity resolves group order from [UpdateBefore]/[UpdateAfter]/OrderFirst/OrderLast edges;
    // a missing edge fails SILENTLY (Unity falls back to an arbitrary stable order). These
    // tests assert that every adjacent pair in the documented pipeline has an EXPLICIT edge,
    // and that no ordering constraint crosses a group boundary (Unity ignores those with only
    // an editor-log warning).
    [TestFixture]
    public sealed class SystemGroupOrderTests
    {
        // The documented SimulationSystemGroup pipeline, in order.
        private static readonly Type[] SimulationPipeline =
        {
            typeof(GameManagerSystemGroup),
            typeof(PlayerSystemGroup),
            typeof(CutsceneSystemGroup),
            typeof(UtilityAISystemGroup),
            typeof(MinionActionSelectionSystemGroup),
            typeof(StateMachineSystemGroup),
            typeof(ItemSystemGroup),
            typeof(MovementSystemGroup),
            typeof(BuildingsSystemGroup),
            typeof(CombatSystemGroup),
            typeof(HealthSystemGroup),
            typeof(DesignSystemGroup),
            typeof(AnimationSystemGroup),
        };

        // The documented LateSimulationSystemGroup pipeline, in order.
        private static readonly Type[] LateSimulationPipeline =
        {
            typeof(SpawnSystemGroup),
            typeof(SpawnInitSystemGroup),
            typeof(SoundSystemGroup),
            typeof(DespawnSystemGroup),
            typeof(SaveSystemGroup),
        };

        // Every nested group and the parent it must declare via [UpdateInGroup].
        private static readonly Dictionary<Type, Type> ChildToParent = new Dictionary<Type, Type>
        {
            { typeof(PlayerInputSystemGroup),         typeof(PlayerSystemGroup) },
            { typeof(NarrativeSystemGroup),           typeof(PlayerSystemGroup) },
            { typeof(DialogueSystemGroup),            typeof(PlayerSystemGroup) },
            { typeof(PlayerEquipmentSystemGroup),     typeof(PlayerSystemGroup) },
            { typeof(UtilityMotivationSystemGroup),   typeof(UtilityAISystemGroup) },
            { typeof(UtilityAwarenessSystemGroup),    typeof(UtilityAISystemGroup) },
            { typeof(ActionSelectionSystemGroup),     typeof(StateMachineSystemGroup) },
            { typeof(ActionExecutionSystemGroup),     typeof(StateMachineSystemGroup) },
            { typeof(ItemEquipSystemGroup),           typeof(ItemSystemGroup) },
            { typeof(ThrownItemSystemGroup),          typeof(ItemSystemGroup) },
            { typeof(MovementCoordinatorSystemGroup), typeof(MovementSystemGroup) },
            { typeof(MovementRoutingSystemGroup),     typeof(MovementSystemGroup) },
            { typeof(MovementFollowerSystemGroup),    typeof(MovementSystemGroup) },
            { typeof(MovementExecutionSystemGroup),   typeof(MovementSystemGroup) },
            { typeof(CombatExecutionSystemGroup),     typeof(CombatSystemGroup) },
            { typeof(CombatReactionSystemGroup),      typeof(CombatSystemGroup) },
            { typeof(AnimationAssignmentSystemGroup), typeof(AnimationSystemGroup) },
        };

        private static UpdateInGroupAttribute GetUpdateInGroup(Type groupType)
        {
            UpdateInGroupAttribute attribute =
                (UpdateInGroupAttribute)Attribute.GetCustomAttribute(groupType, typeof(UpdateInGroupAttribute));
            Assert.IsNotNull(attribute, groupType.Name + " is missing [UpdateInGroup]");
            return attribute;
        }

        private static bool HasExplicitEdge(Type earlierGroup, Type laterGroup)
        {
            foreach (UpdateBeforeAttribute beforeAttribute in
                Attribute.GetCustomAttributes(earlierGroup, typeof(UpdateBeforeAttribute)))
            {
                if (beforeAttribute.SystemType == laterGroup) return true;
            }
            foreach (UpdateAfterAttribute afterAttribute in
                Attribute.GetCustomAttributes(laterGroup, typeof(UpdateAfterAttribute)))
            {
                if (afterAttribute.SystemType == earlierGroup) return true;
            }

            UpdateInGroupAttribute earlierPlacement = GetUpdateInGroup(earlierGroup);
            UpdateInGroupAttribute laterPlacement = GetUpdateInGroup(laterGroup);
            bool earlierRunsFirst = earlierPlacement.OrderFirst && !laterPlacement.OrderFirst;
            bool laterRunsLast = laterPlacement.OrderLast && !earlierPlacement.OrderLast;
            return earlierRunsFirst || laterRunsLast;
        }

        private static void AssertPipelineHasExplicitEdges(Type[] pipeline, Type expectedParentGroup)
        {
            List<string> offenders = new List<string>();
            foreach (Type pipelineGroup in pipeline)
            {
                UpdateInGroupAttribute placement = GetUpdateInGroup(pipelineGroup);
                if (placement.GroupType != expectedParentGroup)
                {
                    offenders.Add(pipelineGroup.Name + " updates in " + placement.GroupType.Name
                        + " but the documented pipeline places it in " + expectedParentGroup.Name);
                }
            }
            for (int pipelineIndex = 0; pipelineIndex < pipeline.Length - 1; pipelineIndex++)
            {
                Type earlierGroup = pipeline[pipelineIndex];
                Type laterGroup = pipeline[pipelineIndex + 1];
                if (!HasExplicitEdge(earlierGroup, laterGroup))
                {
                    offenders.Add("No explicit ordering edge between " + earlierGroup.Name
                        + " and " + laterGroup.Name
                        + " — add [UpdateBefore]/[UpdateAfter] so the pipeline cannot silently reorder");
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void SimulationPipelineEdgesAreExplicit()
        {
            AssertPipelineHasExplicitEdges(SimulationPipeline, typeof(SimulationSystemGroup));
        }

        [Test]
        public void LateSimulationPipelineEdgesAreExplicit()
        {
            AssertPipelineHasExplicitEdges(LateSimulationPipeline, typeof(LateSimulationSystemGroup));
        }

        [Test]
        public void NestedGroupsDeclareTheirDocumentedParent()
        {
            List<string> offenders = new List<string>();
            foreach (KeyValuePair<Type, Type> childAndParent in ChildToParent)
            {
                UpdateInGroupAttribute placement = GetUpdateInGroup(childAndParent.Key);
                if (placement.GroupType != childAndParent.Value)
                {
                    offenders.Add(childAndParent.Key.Name + " updates in " + placement.GroupType.Name
                        + " but is documented as a child of " + childAndParent.Value.Name);
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void GroupOrderingConstraintsNeverCrossGroupBoundaries()
        {
            // Unity IGNORES an [UpdateBefore]/[UpdateAfter] whose target lives in a different
            // parent group — the constraint silently does nothing. Assert none of our declared
            // groups carry such an edge.
            List<Type> allDeclaredGroups = new List<Type>(SimulationPipeline);
            allDeclaredGroups.AddRange(LateSimulationPipeline);
            allDeclaredGroups.AddRange(ChildToParent.Keys);

            List<string> offenders = new List<string>();
            foreach (Type declaredGroup in allDeclaredGroups)
            {
                Type ownParent = GetUpdateInGroup(declaredGroup).GroupType;

                foreach (UpdateBeforeAttribute beforeAttribute in
                    Attribute.GetCustomAttributes(declaredGroup, typeof(UpdateBeforeAttribute)))
                {
                    Type targetParent = GetUpdateInGroup(beforeAttribute.SystemType).GroupType;
                    if (targetParent != ownParent)
                    {
                        offenders.Add(declaredGroup.Name + " has [UpdateBefore(" + beforeAttribute.SystemType.Name
                            + ")] but they live in different parent groups (" + ownParent.Name
                            + " vs " + targetParent.Name + ") — Unity ignores this edge");
                    }
                }
                foreach (UpdateAfterAttribute afterAttribute in
                    Attribute.GetCustomAttributes(declaredGroup, typeof(UpdateAfterAttribute)))
                {
                    Type targetParent = GetUpdateInGroup(afterAttribute.SystemType).GroupType;
                    if (targetParent != ownParent)
                    {
                        offenders.Add(declaredGroup.Name + " has [UpdateAfter(" + afterAttribute.SystemType.Name
                            + ")] but they live in different parent groups (" + ownParent.Name
                            + " vs " + targetParent.Name + ") — Unity ignores this edge");
                    }
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }
    }
}
