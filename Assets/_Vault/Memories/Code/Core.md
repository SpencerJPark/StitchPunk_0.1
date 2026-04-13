---
tags: [memory, code, core, infrastructure]
related: "[[MonoBehaviours]], [[Systems]]"
---

# Core — Context

Core contains infrastructure shared across the project: singleton base classes, render features, save/load, and legacy files.

---

## Singleton Base Classes (`BaseClasses/`)

| Class | Behaviour |
|---|---|
| `Singleton<T>` | Standard singleton; destroyed on scene load |
| `PersistentSingleton<T>` | Survives scene loads (`DontDestroyOnLoad`) |
| `RegulatorSingleton<T>` | Destroys duplicate instances; safe for additive loading |

[[MonoBehaviours]] managers (camera, input, resources) inherit from these.

Also contains `UpdateManager`, `LateUpdateManager`, `FixedUpdateManager` — manual update registration to reduce `Update()` overhead on many MonoBehaviours.

---

## Render Features (`RenderFeatures/`)

Custom URP `ScriptableRendererFeature` implementations:

| Feature | Effect |
|---|---|
| `CelShadingFeature` | Cartoon/toon shading pass |
| `SilhouetteOutlineFeature` | Selection highlight outlines (used by PresentationSystemGroup — see [[Systems]]) |
| `RobertsCrossRenderFeature` | Edge-detection outline for the world |

These are assigned in the URP Renderer asset, not referenced from scripts.

---

## Save / Load (`Saving/`)

Interface-based save system: `IDataService` + `ISerializer`. Current implementation uses `JsonSerializer`. The ECS-side save pipeline (SaveSystemGroup) is in [[Systems]] — this `Core/Saving/` layer provides the serialization utilities it calls. Save file DTO structures are in [[Data]].

---

## Legacy — Do Not Use

| File | Status |
|---|---|
| `RiveAnimator.cs` | Legacy. Was for Rive animation package integration; package changed and broke it. **Move to `Unused/` when convenient.** Do not reference. |
| `Core/Unused/` | All files here are the old MonoBehaviour-based animation system. Replaced by the DOTS animation system — see [[Systems_Animation]]. Do not reference or restore. |
