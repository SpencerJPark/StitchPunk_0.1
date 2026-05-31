using System.Collections.Generic;
using UnityEngine;

// Authoritative list of every BehaviorSO in the game, baked into the enum-indexed BehaviorLibrary
// blob (keyed by BehaviorSO.behaviorType). Mirrors ItemLibrarySO. One asset per scene/game.
[CreateAssetMenu(fileName = "_BehaviorLibrary", menuName = "AI/Behavior Library")]
public class BehaviorLibrarySO : ScriptableObject
{
    public List<BehaviorSO> behaviors = new List<BehaviorSO>();
}
