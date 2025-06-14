using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    [Header("Input Handler")]
    [Tooltip("Drag in the PlayerInputHandler component here")]
    [SerializeField] private PlayerInputHandler inputHandler;

    
    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    /// <summary>
    /// Expose the input handler so zones can poll Move/Interact/etc.
    /// </summary>
    public PlayerInputHandler InputHandler => inputHandler;

    // Switch both the Input System map and the Cinemachine cam
    void SwitchControlZone(string mapName, CameraType cam)
    {
        inputHandler.SwitchActionMap(mapName);
        CameraManager.Instance.SwitchCamera(cam);
    }

    // Convenience methods for your common zones:
    public void SwitchToHero()
        => SwitchControlZone("Player", CameraType.Player);
    
    public void SwitchToZoom()
        => SwitchControlZone("Player", CameraType.PlayerZoom);

    public void SwitchToVehicle()
        => SwitchControlZone("Vehicle", CameraType.Vehicle);

    // public void SwitchToHorde()
    //     => SwitchControlZone("Horde", CameraType.Horde);

    // public void SwitchToMapUI()
    //     => SwitchControlZone("MapUI", CameraType.MapUI);
}
