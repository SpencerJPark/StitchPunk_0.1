// using Unity.Collections;
// using Unity.Entities;
//
// [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
// [UpdateInGroup(typeof(PostBakingSystemGroup))]
// public partial struct FactoryLibraryBakingSystem : ISystem
// {
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<FactoryLibraryReference>();
//     }
//
//     public void OnUpdate(ref SystemState state)
//     {
//         FactoryLibrarySO librarySO = null;
//         foreach (RefRO<FactoryLibraryReference> reference in SystemAPI.Query<RefRO<FactoryLibraryReference>>())
//         {
//             librarySO = reference.ValueRO.library.Value;
//             break;
//         }
//
//         if (librarySO == null || librarySO.recipes == null || librarySO.recipes.Length == 0) return;
//
//         using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
//         ref FactoryLibraryBlob root = ref builder.ConstructRoot<FactoryLibraryBlob>();
//         BlobBuilderArray<RecipeBlob> recipesBuilder = builder.Allocate(ref root.recipes, librarySO.recipes.Length);
//
//         for (int i = 0; i < librarySO.recipes.Length; i++)
//         {
//             ProductionRecipeSO recipeSO = librarySO.recipes[i];
//             if (recipeSO == null) continue;
//
//             recipesBuilder[i].structureType     = recipeSO.structureType;
//             recipesBuilder[i].durationSeconds = recipeSO.durationSeconds;
//             recipesBuilder[i].workersRequired = recipeSO.workersRequired;
//
//             int inputCount = recipeSO.inputs != null ? recipeSO.inputs.Length : 0;
//             BlobBuilderArray<FactoryItemType> inputsBuilder = builder.Allocate(ref recipesBuilder[i].inputs, inputCount);
//             for (int j = 0; j < inputCount; j++)
//                 inputsBuilder[j] = recipeSO.inputs[j];
//
//             int outputCount = recipeSO.outputs != null ? recipeSO.outputs.Length : 0;
//             BlobBuilderArray<FactoryItemType> outputsBuilder = builder.Allocate(ref recipesBuilder[i].outputs, outputCount);
//             for (int j = 0; j < outputCount; j++)
//                 outputsBuilder[j] = recipeSO.outputs[j];
//         }
//
//         BlobAssetReference<FactoryLibraryBlob> blobRef =
//             builder.CreateBlobAssetReference<FactoryLibraryBlob>(Allocator.Persistent);
//
//         foreach (RefRW<FactoryLibrary> holder in SystemAPI.Query<RefRW<FactoryLibrary>>())
//         {
//             if (holder.ValueRO.blob.IsCreated)
//                 holder.ValueRW.blob.Dispose();
//
//             holder.ValueRW.blob = blobRef;
//         }
//     }
//
//     public void OnDestroy(ref SystemState state)
//     {
//         foreach (RefRW<FactoryLibrary> holder in SystemAPI.Query<RefRW<FactoryLibrary>>())
//         {
//             if (holder.ValueRO.blob.IsCreated)
//                 holder.ValueRW.blob.Dispose();
//         }
//     }
// }
