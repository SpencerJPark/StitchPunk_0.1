using UnityEngine;
using Cysharp.Threading.Tasks;

[DefaultExecutionOrder(-100)] // Runs early
public class CharacterInitializer : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private RiveAnimator riveAnimator;
    [SerializeField] private CharacterControllerBase controller;

    [Header("Optional")]
    [SerializeField] private CharacterStateData defaultState;
    [SerializeField] private CharacterDesignBase design;
    [SerializeField] private MonoBehaviour facingComponent; // Must implement IFacingController

    private bool isInitialized = false;

    private async void Start()
    {
        if (isInitialized) return;

        await InitializeAsync();
    }

    public async UniTask InitializeAsync()
    {
        if (riveAnimator == null)
        {
            Debug.LogError($"{name} is missing RiveAnimator!");
            return;
        }

        // Wait for Rive to be fully ready
        await riveAnimator.WaitForReadyAsync();

        // Apply visual customization (skin, hair, etc.)
        if (design != null)
            design.ApplyCustomization();

        // Apply default state
        if (controller != null && defaultState != null)
            controller.ApplyState(defaultState);

        // // Set default facing direction
        // if (facingComponent is IFacingController facing)
        //     facing.SetDefaultFacing();

        isInitialized = true;
        Debug.Log($"{name} initialized successfully.");
    }
}

