using System.Collections.Generic;
using UnityEngine;

// A unit archetype's action set. Baked into AISOBlob.brains, indexed by unitType.
[CreateAssetMenu(fileName = "BrainSO", menuName = "AI/Brain SO")]
public class BrainSO : ScriptableObject
{
    [Tooltip("Which UnitType this brain belongs to (blob slot index)")]
    public UnitType unitType;

    [Tooltip("Inherit every action/module of this brain first (e.g. HumanBaseBrain).")]
    public BrainSO baseBrain;

    [Tooltip("Abilities added on top of the base brain.")]
    public List<BehaviourModuleSO> modules = new List<BehaviourModuleSO>();

    [Tooltip("Legacy/direct actions; prefer modules")]
    public List<UtilityActionSO> actions = new List<UtilityActionSO>();

    // Flattens base chain (root first) then this brain; baking and the runtime lookup both rely on base-first order.
    public void CollectActions(List<UtilityActionSO> collectedActions, List<string> problems)
    {
        List<BrainSO> chainRootFirst = new List<BrainSO>();
        HashSet<BrainSO> visitedBrains = new HashSet<BrainSO>();
        BrainSO current = this;
        while (current != null)
        {
            if (!visitedBrains.Add(current))
            {
                problems.Add("Brain '" + name + "' has a baseBrain cycle");
                break;
            }
            chainRootFirst.Insert(0, current);
            current = current.baseBrain;
        }

        foreach (BrainSO brain in chainRootFirst)
        {
            if (brain.actions != null)
            {
                foreach (UtilityActionSO action in brain.actions)
                    AddIfNewAction(collectedActions, action);
            }

            if (brain.modules == null) continue;
            foreach (BehaviourModuleSO module in brain.modules)
            {
                if (module == null) continue;
                if (module.action == null)
                {
                    problems.Add("Module '" + module.name + "' on brain '" + brain.name + "' has no action");
                    continue;
                }
                AddIfNewAction(collectedActions, module.action);
            }
        }

        Dictionary<ActionType, UtilityActionSO> firstActionByType = new Dictionary<ActionType, UtilityActionSO>();
        foreach (UtilityActionSO action in collectedActions)
        {
            if (!firstActionByType.TryGetValue(action.actionType, out UtilityActionSO firstAction))
            {
                firstActionByType[action.actionType] = action;
                continue;
            }
            problems.Add("Brain '" + name + "' has two actions for " + action.actionType
                + ": '" + firstAction.name + "' and '" + action.name + "' — the first wins");
        }
    }

    private static void AddIfNewAction(List<UtilityActionSO> collectedActions, UtilityActionSO action)
    {
        if (action == null || collectedActions.Contains(action)) return;
        collectedActions.Add(action);
    }
}
