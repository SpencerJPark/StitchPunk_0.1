// using System.Collections.Generic;
// using UnityEngine;
// using Rive.Components;

// public class AtlasManager : MonoBehaviour
// {
//     [Header("Atlas Settings")]
//     [SerializeField] private AtlasRenderTargetStrategy atlasPrefab; // Prefab of atlas render strategy
//     [SerializeField] private int initialPoolSize = 2;
//     [SerializeField] private int maxPanelsPerAtlas = 16;

//     private readonly List<AtlasRenderTargetStrategy> atlasPool = new List<AtlasRenderTargetStrategy>();

//     private void Awake()
//     {
//         // Pre-populate the pool
//         for (int i = 0; i < initialPoolSize; i++)
//         {
//             CreateNewAtlas();
//         }
//     }

//     /// <summary>
//     /// Registers a RiveCharacter with an atlas.
//     /// </summary>
//     public void RegisterCharacter(RiveCharacter character)
//     {
//         AtlasRenderTargetStrategy targetAtlas = FindAvailableAtlas();

//         if (targetAtlas == null)
//         {
//             // No atlas has free space — create a new one
//             targetAtlas = CreateNewAtlas();
//         }

//         targetAtlas.RegisterPanel(character.Panel);
//         character.CurrentAtlas = targetAtlas;
//     }

//     /// <summary>
//     /// Unregisters a RiveCharacter from its atlas.
//     /// </summary>
//     public void UnregisterCharacter(RiveCharacter character)
//     {
//         if (character.CurrentAtlas != null)
//         {
//             character.CurrentAtlas.UnregisterPanel(character.Panel);
//             character.CurrentAtlas = null;
//         }
//     }

//     /// <summary>
//     /// Finds an atlas with available space.
//     /// </summary>
//     private AtlasRenderTargetStrategy FindAvailableAtlas()
//     {
//         foreach (var atlas in atlasPool)
//         {
//             if (atlas.RegisteredPanelCount < maxPanelsPerAtlas)
//             {
//                 return atlas;
//             }
//         }
//         return null;
//     }

//     /// <summary>
//     /// Instantiates a new atlas and adds it to the pool.
//     /// </summary>
//     private AtlasRenderTargetStrategy CreateNewAtlas()
//     {
//         var atlas = Instantiate(atlasPrefab, transform);
//         atlas.name = $"Atlas_{atlasPool.Count}";
//         atlas.Initialize(maxPanelsPerAtlas);
//         atlasPool.Add(atlas);
//         return atlas;
//     }
// }
