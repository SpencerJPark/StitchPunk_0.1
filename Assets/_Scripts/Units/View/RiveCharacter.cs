// using UnityEngine;
// using Rive.Components;

// [DisallowMultipleComponent]
// public class RiveCharacter : MonoBehaviour
// {
//     [Tooltip("Reference to the Atlas Manager this character should register to.")]
//     [SerializeField] private AtlasManager _atlasManager;

//     public AtlasManager atlasManager
//     {
//         get => _atlasManager;
//         set
//         {
//             if (_atlasManager != value)
//             {
//                 // Unregister from old manager
//                 Unregister();

//                 _atlasManager = value;

//                 // Register to new manager
//                 TryRegister();
//             }
//         }
//     }

//     [Tooltip("High priority characters will be assigned first.")]
//     public bool highPriority = false;

//     public RivePanel RivePanel { get; private set; }

//     public delegate void CharacterDestroyed(RiveCharacter character);
//     public event CharacterDestroyed OnDestroyed;

//     private bool isRegistered = false;

//     private void Awake()
//     {
//         TryRegister();
//     }

//     private void OnEnable()
//     {
//         TryRegister();
//     }

//     private void OnDisable()
//     {
//         Unregister();
//     }

//     private void OnDestroy()
//     {
//         Unregister();
//         OnDestroyed?.Invoke(this);
//     }

//     /// <summary>
//     /// Attempts to register the character to the AtlasManager.
//     /// </summary>
//     public void TryRegister()
//     {
//         if (!isRegistered && _atlasManager != null)
//         {
//             _atlasManager.RegisterCharacter(this, highPriority);
//             isRegistered = true;
//         }
//     }

//     /// <summary>
//     /// Unregisters the character from the AtlasManager.
//     /// </summary>
//     public void Unregister()
//     {
//         if (isRegistered && _atlasManager != null)
//         {
//             _atlasManager.UnregisterCharacter(this);
//             isRegistered = false;
//             RivePanel = null;
//         }
//     }
// }
