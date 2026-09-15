using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests
{
    // Pins AIUtils.ResolveOrderedAttack's order-wins semantics: the first AvailableAttack entry
    // whose actionType has a def in the unit's brain wins, entries without a def are skipped.
    [TestFixture]
    public sealed class AttackResolutionTests
    {
        private static BlobAssetReference<BrainLibraryBlob> BuildTestLibrary()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref BrainLibraryBlob root = ref builder.ConstructRoot<BrainLibraryBlob>();

            int brainCount = BlobLibraryUtils.EnumCount<UnitType>();
            BlobBuilderArray<BrainBlob> brainsBuilder = builder.Allocate(ref root.brains, brainCount);
            for (int brainIndex = 0; brainIndex < brainCount; brainIndex++)
            {
                UnitType unitType = (UnitType)brainIndex;
                brainsBuilder[brainIndex].unitType = unitType;

                if (unitType == UnitType.MaleCitizen)
                {
                    BlobBuilderArray<int> maleCitizenActionDefIndices =
                        builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 2);
                    maleCitizenActionDefIndices[0] = 1;
                    maleCitizenActionDefIndices[1] = 2;
                }
                else
                {
                    builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 0);
                }
            }

            BlobBuilderArray<ActionDefBlob> actionDefsBuilder = builder.Allocate(ref root.actionDefs, 3);
            actionDefsBuilder[0].actionType = ActionType.Wander;
            actionDefsBuilder[1].actionType = ActionType.ProjectileSingle;
            actionDefsBuilder[2].actionType = ActionType.MeleeSingle;
            builder.Allocate(ref actionDefsBuilder[0].considerations, 0);
            builder.Allocate(ref actionDefsBuilder[1].considerations, 0);
            builder.Allocate(ref actionDefsBuilder[2].considerations, 0);

            BlobAssetReference<BrainLibraryBlob> blob =
                builder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        [Test]
        public void FirstEntryWithADef_Wins()
        {
            BlobAssetReference<BrainLibraryBlob> library = BuildTestLibrary();
            NativeArray<AvailableAttack> attacks = new NativeArray<AvailableAttack>(2, Allocator.Temp);
            try
            {
                attacks[0] = new AvailableAttack { actionType = ActionType.MeleeSingle };
                attacks[1] = new AvailableAttack { actionType = ActionType.ProjectileSingle };

                bool resolved = AIUtils.ResolveOrderedAttack(
                    ref library.Value,
                    UnitType.MaleCitizen,
                    attacks,
                    out ActionType resolvedActionType,
                    out int resolvedDefIndex);

                Assert.IsTrue(resolved);
                Assert.AreEqual(ActionType.MeleeSingle, resolvedActionType);
                Assert.AreEqual(2, resolvedDefIndex);
            }
            finally
            {
                attacks.Dispose();
                library.Dispose();
            }
        }

        [Test]
        public void EntryWithoutADef_IsSkipped()
        {
            BlobAssetReference<BrainLibraryBlob> library = BuildTestLibrary();
            NativeArray<AvailableAttack> attacks = new NativeArray<AvailableAttack>(2, Allocator.Temp);
            try
            {
                attacks[0] = new AvailableAttack { actionType = ActionType.Throw };
                attacks[1] = new AvailableAttack { actionType = ActionType.ProjectileSingle };

                bool resolved = AIUtils.ResolveOrderedAttack(
                    ref library.Value,
                    UnitType.MaleCitizen,
                    attacks,
                    out ActionType resolvedActionType,
                    out int resolvedDefIndex);

                Assert.IsTrue(resolved);
                Assert.AreEqual(ActionType.ProjectileSingle, resolvedActionType);
                Assert.AreEqual(1, resolvedDefIndex);
            }
            finally
            {
                attacks.Dispose();
                library.Dispose();
            }
        }

        [Test]
        public void EmptyBuffer_ReturnsFalse()
        {
            BlobAssetReference<BrainLibraryBlob> library = BuildTestLibrary();
            NativeArray<AvailableAttack> attacks = new NativeArray<AvailableAttack>(0, Allocator.Temp);
            try
            {
                bool resolved = AIUtils.ResolveOrderedAttack(
                    ref library.Value,
                    UnitType.MaleCitizen,
                    attacks,
                    out ActionType resolvedActionType,
                    out int resolvedDefIndex);

                Assert.IsFalse(resolved);
                Assert.AreEqual(-1, resolvedDefIndex);
            }
            finally
            {
                attacks.Dispose();
                library.Dispose();
            }
        }
    }
}
