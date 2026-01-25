using UnityEngine;

public class GameAssets : MonoBehaviour {

    public const int DEFAULT_LAYER = 0;
    public const int GROUND_LAYER = 3;
    public const int UNITS_LAYER = 6;
    public const int STRUCTURES_LAYER = 7;
    public const int WALLS_LAYER = 8;
    public const int PATHFINDING_HEAVY_LAYER = 9;
    public const int OBJECTS_LAYER = 10;
    public const int OUTLINE_LAYER = 11;
    public const int INTERACTABLE_LAYER = 12;



    public static GameAssets Instance { get; private set; }



    private void Awake() {
        Instance = this;
    }


    [Header("Animation")]
    [Range(1, 60)]
    public int animationFrameRate = 24;
    
    public UnitTypeListSO unitTypeListSO;
    public BuildingTypeListSO buildingTypeListSO;


}