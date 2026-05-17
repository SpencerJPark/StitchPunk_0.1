using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class InteractionLibraryAuthoring : MonoBehaviour
{
    public InteractionLibrarySO library;

    public class Baker : Baker<InteractionLibraryAuthoring>
    {
        public override void Bake(InteractionLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            // Register each InteractionSO as a bake dependency so saved changes
            // to individual SOs trigger a rebake automatically.
            Dictionary<int, InteractionSO> soByIndex = new Dictionary<int, InteractionSO>();
            foreach (InteractionSO so in authoring.library.interactions)
            {
                if (so == null) continue;
                DependsOn(so);
                soByIndex[(int)so.interactionActionType] = so;
            }

            int typeCount = System.Enum.GetValues(typeof(ActionType)).Length;

            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref InteractionLibraryBlob root = ref builder.ConstructRoot<InteractionLibraryBlob>();
            BlobBuilderArray<InteractionBlob> interactionsBuilder =
                builder.Allocate(ref root.interactions, typeCount);

            for (int i = 0; i < typeCount; i++)
            {
                if (soByIndex.TryGetValue(i, out InteractionSO so))
                {
                    interactionsBuilder[i].actionType          = so.interactionActionType;
                    interactionsBuilder[i].priority            = so.priority;
                    interactionsBuilder[i].maxOccupants        = so.maxOccupants;
                    interactionsBuilder[i].range               = so.range;
                    interactionsBuilder[i].satisfiedMotivation = so.motivationSatisfaction.motivationType;
                    interactionsBuilder[i].restorationAmount   = so.motivationSatisfaction.value;
                    interactionsBuilder[i].duration            = so.duration;

                    int factionCount = so.factionsThatCanInteractWith != null
                        ? so.factionsThatCanInteractWith.Length : 0;
                    BlobBuilderArray<FactionType> factionsBuilder =
                        builder.Allocate(ref interactionsBuilder[i].allowedFactions, factionCount);
                    for (int f = 0; f < factionCount; f++)
                        factionsBuilder[f] = so.factionsThatCanInteractWith[f];
                }
                else
                {
                    interactionsBuilder[i].actionType          = (ActionType)i;
                    interactionsBuilder[i].priority            = 0;
                    interactionsBuilder[i].maxOccupants        = 1;
                    interactionsBuilder[i].range               = 1.5f;
                    interactionsBuilder[i].satisfiedMotivation = MotivationType.None;
                    interactionsBuilder[i].restorationAmount   = 0f;
                    interactionsBuilder[i].duration            = 4f;
                    builder.Allocate(ref interactionsBuilder[i].allowedFactions, 0);
                }
            }

            BlobAssetReference<InteractionLibraryBlob> blobRef =
                builder.CreateBlobAssetReference<InteractionLibraryBlob>(Allocator.Persistent);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new InteractionLibrary { library = blobRef });
        }
    }
}
