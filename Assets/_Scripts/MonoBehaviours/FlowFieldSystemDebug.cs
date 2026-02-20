// using Unity.Entities;
// using Unity.Mathematics;
// using UnityEngine;
//
// public class GridSystemDebug : MonoBehaviour {
//
//     public static GridSystemDebug Instance { get; private set; }
//
//     [SerializeField] private Transform debugPrefab;
//     [SerializeField] private Sprite circleSprite;
//     [SerializeField] private Sprite arrowSprite;
//
//     private bool isInit;
//     private GridSystemDebugSingle[,] gridSystemDebugSingleArray;
//
//     private void Awake() {
//         Instance = this;
//     }
//
//     public void InitializeGrid(GridSystem.GridSystemData gridSystemData) {
//         if (isInit) {
//             return;
//         }
//         isInit = true;
//
//         gridSystemDebugSingleArray = new GridSystemDebugSingle[gridSystemData.width, gridSystemData.height];
//         for (int x = 0; x < gridSystemData.width; x++) {
//             for (int y = 0; y < gridSystemData.height; y++) {
//                 Transform debugTransform = Instantiate(debugPrefab);
//                 GridSystemDebugSingle gridSystemDebugSingle = debugTransform.GetComponent<GridSystemDebugSingle>();
//                 gridSystemDebugSingle.Setup(x, y, gridSystemData.gridNodeSize);
//
//                 gridSystemDebugSingleArray[x, y] = gridSystemDebugSingle;
//             }
//         }
//     }
//
//     public void UpdateGrid(GridSystem.GridSystemData gridSystemData, GridSystem.FlowFieldDataArrays gridDataArrays) {
//         // Get the most recently used grid index
//         int gridIndex = gridSystemData.nextGridIndex - 1;
//         if (gridIndex < 0) {
//             gridIndex = GridSystem.FLOW_FIELD_MAP_COUNT - 1;
//         }
//         
//         // Check if this flow field is valid
//         if (!gridDataArrays.isValid[gridIndex]) {
//             // Try to find any valid flow field to display
//             gridIndex = -1;
//             for (int i = 0; i < GridSystem.FLOW_FIELD_MAP_COUNT; i++) {
//                 if (gridDataArrays.isValid[i]) {
//                     gridIndex = i;
//                     break;
//                 }
//             }
//             
//             // No valid flow fields, just show cost map
//             if (gridIndex < 0) {
//                 UpdateGridCostMapOnly(gridSystemData, gridDataArrays);
//                 return;
//             }
//         }
//
//         int cellCount = gridSystemData.width * gridSystemData.height;
//         int2 targetPosition = gridDataArrays.targetPositions[gridIndex];
//
//         for (int x = 0; x < gridSystemData.width; x++) {
//             for (int y = 0; y < gridSystemData.height; y++) {
//                 GridSystemDebugSingle gridSystemDebugSingle = gridSystemDebugSingleArray[x, y];
//
//                 int localIndex = GridSystem.CalculateIndex(x, y, gridSystemData.width);
//                 int globalIndex = gridIndex * cellCount + localIndex;
//                 
//                 byte cost = gridDataArrays.costMap[localIndex];
//                 float2 vector = gridDataArrays.vectors[globalIndex];
//                 int bestCost = gridDataArrays.bestCosts[globalIndex];
//
//                 // Check if this is the target
//                 if (x == targetPosition.x && y == targetPosition.y) {
//                     gridSystemDebugSingle.SetSprite(circleSprite);
//                     gridSystemDebugSingle.SetColor(Color.green);
//                 }
//                 else if (cost == GridSystem.WALL_COST) {
//                     gridSystemDebugSingle.SetSprite(circleSprite);
//                     gridSystemDebugSingle.SetColor(Color.black);
//                 }
//                 else if (cost == GridSystem.HEAVY_COST) {
//                     gridSystemDebugSingle.SetSprite(arrowSprite);
//                     gridSystemDebugSingle.SetColor(Color.yellow);
//                     gridSystemDebugSingle.SetSpriteRotation(
//                         Quaternion.LookRotation(new float3(vector.x, 0, vector.y), Vector3.up));
//                 }
//                 else {
//                     gridSystemDebugSingle.SetSprite(arrowSprite);
//                     gridSystemDebugSingle.SetColor(Color.white);
//                     gridSystemDebugSingle.SetSpriteRotation(
//                         Quaternion.LookRotation(new float3(vector.x, 0, vector.y), Vector3.up));
//                 }
//             }
//         }
//     }
//     
//     private void UpdateGridCostMapOnly(GridSystem.GridSystemData gridSystemData, GridSystem.FlowFieldDataArrays gridDataArrays) {
//         for (int x = 0; x < gridSystemData.width; x++) {
//             for (int y = 0; y < gridSystemData.height; y++) {
//                 GridSystemDebugSingle gridSystemDebugSingle = gridSystemDebugSingleArray[x, y];
//
//                 int localIndex = GridSystem.CalculateIndex(x, y, gridSystemData.width);
//                 byte cost = gridDataArrays.costMap[localIndex];
//
//                 gridSystemDebugSingle.SetSprite(circleSprite);
//                 
//                 if (cost == GridSystem.WALL_COST) {
//                     gridSystemDebugSingle.SetColor(Color.black);
//                 }
//                 else if (cost == GridSystem.HEAVY_COST) {
//                     gridSystemDebugSingle.SetColor(Color.yellow);
//                 }
//                 else {
//                     gridSystemDebugSingle.SetColor(Color.gray);
//                 }
//             }
//         }
//     }
// }