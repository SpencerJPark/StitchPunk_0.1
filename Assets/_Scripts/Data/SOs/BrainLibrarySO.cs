using System.Collections.Generic;
using UnityEngine;

// Authoritative list of every BrainSO in the game, baked into the enum-indexed BrainLibrary
// blob (keyed by BrainSO.behaviorType). Mirrors ItemLibrarySO. One asset per scene/game.
[CreateAssetMenu(fileName = "_BrainLibrary", menuName = "AI/Brain Library")]
public class BrainLibrarySO : ScriptableObject
{
    public List<BrainSO> brains = new List<BrainSO>();
}