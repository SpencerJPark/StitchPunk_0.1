using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class BrainCompositionTests
    {
        private readonly List<Object> createdAssets = new List<Object>();

        [TearDown]
        public void DestroyCreatedAssets()
        {
            foreach (Object createdAsset in createdAssets)
            {
                Object.DestroyImmediate(createdAsset);
            }

            createdAssets.Clear();
        }

        [Test]
        public void CollectActions_BaseActionsComeBeforeChildModuleActions()
        {
            UtilityActionSO wanderAction = CreateAction(ActionType.Wander);
            UtilityActionSO sitAction = CreateAction(ActionType.Sit);

            BrainSO baseBrain = CreateBrain();
            baseBrain.actions.Add(wanderAction);

            BrainSO childBrain = CreateBrain();
            childBrain.baseBrain = baseBrain;
            childBrain.modules.Add(CreateModule(sitAction));

            List<UtilityActionSO> collectedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            childBrain.CollectActions(collectedActions, problems);

            Assert.AreEqual(2, collectedActions.Count);
            Assert.AreSame(wanderAction, collectedActions[0]);
            Assert.AreSame(sitAction, collectedActions[1]);
            Assert.AreEqual(0, problems.Count);
        }

        [Test]
        public void CollectActions_SameActionInstanceInBaseAndChild_AppearsOnce()
        {
            UtilityActionSO wanderAction = CreateAction(ActionType.Wander);

            BrainSO baseBrain = CreateBrain();
            baseBrain.actions.Add(wanderAction);

            BrainSO childBrain = CreateBrain();
            childBrain.baseBrain = baseBrain;
            childBrain.actions.Add(wanderAction);

            List<UtilityActionSO> collectedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            childBrain.CollectActions(collectedActions, problems);

            Assert.AreEqual(1, collectedActions.Count);
        }

        [Test]
        public void CollectActions_BaseBrainCycle_ReturnsAndReportsCycleProblem()
        {
            BrainSO brainA = CreateBrain();
            BrainSO brainB = CreateBrain();
            brainA.baseBrain = brainB;
            brainB.baseBrain = brainA;

            List<UtilityActionSO> collectedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            brainA.CollectActions(collectedActions, problems);

            Assert.IsTrue(problems.Exists(problem => problem.ToLowerInvariant().Contains("cycle")),
                "Expected a problem mentioning a cycle");
        }

        [Test]
        public void CollectActions_TwoDifferentActionsWithSameActionType_ReportsProblemAndKeepsBoth()
        {
            UtilityActionSO firstSleepAction = CreateAction(ActionType.Sleep);
            UtilityActionSO secondSleepAction = CreateAction(ActionType.Sleep);

            BrainSO baseBrain = CreateBrain();
            baseBrain.actions.Add(firstSleepAction);

            BrainSO childBrain = CreateBrain();
            childBrain.baseBrain = baseBrain;
            childBrain.actions.Add(secondSleepAction);

            List<UtilityActionSO> collectedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            childBrain.CollectActions(collectedActions, problems);

            Assert.AreEqual(2, collectedActions.Count);
            Assert.IsTrue(problems.Exists(problem => problem.Contains(ActionType.Sleep.ToString())),
                "Expected a problem naming the duplicated ActionType");
        }

        [Test]
        public void CollectActions_ModuleBundleAddsEveryAction()
        {
            UtilityActionSO wanderAction = CreateAction(ActionType.Wander);
            UtilityActionSO sitAction = CreateAction(ActionType.Sit);

            BehaviourModuleSO bundleModule = CreateModule(wanderAction);
            bundleModule.actions.Add(sitAction);

            BrainSO brain = CreateBrain();
            brain.modules.Add(bundleModule);

            List<UtilityActionSO> collectedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            brain.CollectActions(collectedActions, problems);

            Assert.AreEqual(2, collectedActions.Count);
            Assert.AreSame(wanderAction, collectedActions[0]);
            Assert.AreSame(sitAction, collectedActions[1]);
            Assert.AreEqual(0, problems.Count);
        }

        [Test]
        public void TryGetPriorityOverride_MostDerivedBrainWins()
        {
            BrainSO baseBrain = CreateBrain();
            baseBrain.priorityOverrides.Add(new ActionPriorityOverride { action = ActionType.Wander, priority = 2 });

            BrainSO childBrain = CreateBrain();
            childBrain.baseBrain = baseBrain;
            childBrain.priorityOverrides.Add(new ActionPriorityOverride { action = ActionType.Wander, priority = 4 });

            int childPriority;
            int basePriority;
            int missingPriority;

            Assert.IsTrue(childBrain.TryGetPriorityOverride(ActionType.Wander, out childPriority));
            Assert.AreEqual(4, childPriority);
            Assert.IsTrue(baseBrain.TryGetPriorityOverride(ActionType.Wander, out basePriority));
            Assert.AreEqual(2, basePriority);
            Assert.IsFalse(childBrain.TryGetPriorityOverride(ActionType.Sit, out missingPriority));
            Assert.AreEqual(0, missingPriority);
        }

        private UtilityActionSO CreateAction(ActionType actionType)
        {
            UtilityActionSO action = ScriptableObject.CreateInstance<UtilityActionSO>();
            action.actionType = actionType;
            createdAssets.Add(action);
            return action;
        }

        private BrainSO CreateBrain()
        {
            BrainSO brain = ScriptableObject.CreateInstance<BrainSO>();
            createdAssets.Add(brain);
            return brain;
        }

        private BehaviourModuleSO CreateModule(UtilityActionSO action)
        {
            BehaviourModuleSO module = ScriptableObject.CreateInstance<BehaviourModuleSO>();
            module.actions.Add(action);
            createdAssets.Add(module);
            return module;
        }
    }
}
