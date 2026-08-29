#if UNITY_EDITOR
using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

// The game's half of the toolkit's direction-set context seam: flattens every UnitSO's animation
// mappings into "<Unit> · <state>" entries so picking one in the 2D Direction Sets panel loads the
// set, the rig and the actor's turn granularity in a single click. The toolkit knows nothing about
// UnitSO or ActionType — it only ever sees the labels and the three assets this hands over.
[InitializeOnLoad]
public sealed class UnitDirectionSetContextProvider : IDirectionSetContextProvider
{
    // [InitializeOnLoad] is load-bearing, not decoration: without it this static constructor only
    // runs the first time something touches the type, which nothing ever does — so the panel's Unit
    // Context dropdown would stay hidden on every fresh domain load.
    static UnitDirectionSetContextProvider()
    {
        DirectionSetsPanel.SetContextProvider(new UnitDirectionSetContextProvider());
    }

    public IReadOnlyList<DirectionSetContextEntry> GetEntries()
    {
        List<DirectionSetContextEntry> entries = new List<DirectionSetContextEntry>();
        List<string> unitsWithoutRig = new List<string>();

        string[] unitGuids = AssetDatabase.FindAssets("t:UnitSO");
        for (int guidIndex = 0; guidIndex < unitGuids.Length; guidIndex++)
        {
            string unitPath = AssetDatabase.GUIDToAssetPath(unitGuids[guidIndex]);
            UnitSO unit = AssetDatabase.LoadAssetAtPath<UnitSO>(unitPath);
            if (unit == null)
            {
                continue;
            }

            RigAsset unitRig = ResolveRig(unit);
            if (unitRig == null)
            {
                unitsWithoutRig.Add(unit.name);
            }

            AddEntry(entries, unit, unitRig, "Idle", unit.idleAnimation);
            AddEntry(entries, unit, unitRig, "Moving", unit.movingAnimation);

            for (int stanceIndex = 0;
                 unit.stanceAnimations != null && stanceIndex < unit.stanceAnimations.Length;
                 stanceIndex++)
            {
                StanceAnimationMapping stance = unit.stanceAnimations[stanceIndex];
                AddEntry(entries, unit, unitRig, stance.stance + " Idle", stance.idleAnimation);
                AddEntry(entries, unit, unitRig, stance.stance + " Moving", stance.movingAnimation);
            }

            for (int actionIndex = 0;
                 unit.actionAnimations != null && actionIndex < unit.actionAnimations.Length;
                 actionIndex++)
            {
                ActionAnimationMapping action = unit.actionAnimations[actionIndex];
                AddEntry(entries, unit, unitRig, action.action.ToString(), action.animation);
            }
        }

        // One consolidated line rather than one per unit: a project mid-migration has many, and a
        // console full of them buries whatever the author was actually looking for.
        if (unitsWithoutRig.Count > 0)
        {
            Debug.LogWarning(
                "[2D Direction Sets] No rig resolved for " + unitsWithoutRig.Count + " unit(s) — "
                + "their entries load a set but no preview rig. Each needs a prefab carrying an "
                + "ActorAuthoring with a rig assigned: " + string.Join(", ", unitsWithoutRig));
        }

        return entries;
    }

    private static void AddEntry(
        List<DirectionSetContextEntry> entries,
        UnitSO unit,
        RigAsset unitRig,
        string stateLabel,
        DirectionSetAsset directionSet)
    {
        entries.Add(new DirectionSetContextEntry
        {
            label = unit.name + " · " + stateLabel,
            set = directionSet,
            previewRig = unitRig,
            actorDirections = unit.animationDirections,
        });
    }

    // The prefab is the runtime source of truth for which rig a unit animates on (UnitSO's own rig
    // field is validate-only), so the preview reads it from the same place the game does.
    private static RigAsset ResolveRig(UnitSO unit)
    {
        if (unit.prefab == null)
        {
            return null;
        }
        ActorAuthoring actor = unit.prefab.GetComponentInChildren<ActorAuthoring>(true);
        return actor != null ? actor.rig : null;
    }
}
#endif
