using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class UnitBrainCoverageTests
    {
        private const string UnitLibraryAssetPath = "Assets/ScriptableObjects/Units/_UnitLibrary.asset";
        private const string BrainLibraryAssetPath = "Assets/ScriptableObjects/AI/Brains/_BrainLibrary.asset";

        // Without a BrainSO the AI scores no actions, so a player-controllable unit silently ignores move and attack orders.
        [Test]
        public void EveryPlayerControllableUnit_HasABrain()
        {
            UnitLibrarySO unitLibrary = AssetDatabase.LoadAssetAtPath<UnitLibrarySO>(UnitLibraryAssetPath);
            BrainLibrarySO brainLibrary = AssetDatabase.LoadAssetAtPath<BrainLibrarySO>(BrainLibraryAssetPath);
            Assert.IsNotNull(unitLibrary, "Could not load " + UnitLibraryAssetPath);
            Assert.IsNotNull(brainLibrary, "Could not load " + BrainLibraryAssetPath);

            HashSet<UnitType> unitTypesWithBrain = new HashSet<UnitType>();
            foreach (BrainSO brain in brainLibrary.brains)
            {
                if (brain != null)
                {
                    unitTypesWithBrain.Add(brain.unitType);
                }
            }

            List<string> missingList = new List<string>();
            foreach (UnitSO unit in unitLibrary.units)
            {
                if (unit != null && unit.canBePlayerControlled && !unitTypesWithBrain.Contains(unit.unitType))
                {
                    missingList.Add(unit.unitType.ToString());
                }
            }

            Assert.IsEmpty(missingList, "Player-controllable units with no BrainSO in _BrainLibrary: " + string.Join(", ", missingList));
        }
    }
}
