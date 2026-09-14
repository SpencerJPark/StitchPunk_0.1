# Camera Sync

One MonoBehaviour, `ToolkitCameraSync`, that writes the `AnimationToolkitCameraData` singleton from a
camera every frame. Billboarded rigs and distance LOD both wait for that singleton, and the package
never reads a `Camera` itself, because it cannot know which of your cameras matters.

## Run it

1. Import this sample from the Package Manager.
2. Add `ToolkitCameraSync` to any GameObject in a scene that has toolkit actors. A manager object is
   fine; it does not need to be the camera.
3. Leave **Camera Override** empty to follow `Camera.main`, or assign the camera billboards should face.
4. Enter Play mode. The console warning "no AnimationToolkitCameraData singleton after 120 frames" no
   longer appears.

## What it demonstrates

- **Finding or creating the singleton.** It reuses an existing `AnimationToolkitCameraData` entity if
  something else already made one, and creates it otherwise.
- **Surviving a recreated world.** It finds the entity again whenever the default world changes or the
  entity is gone, so a scene reload or a play session without a domain reload never writes to a stale
  entity.
- **A pass-invariant forward.** `forward` comes from the camera's transform, not a render matrix, so it
  is the same in every render pass, including the shadow pass.

## What it deliberately does not do

- It does not write the `_ToolkitCameraForward` shader global that the shader billboard path reads; see
  `Documentation~/shader-contract.md`.
- It does not choose between cameras. With Cinemachine, `Camera.main` already follows whichever virtual
  camera is live, so one writer covers every camera a game has.
- Keep one writer per world. Two writers on different cameras overwrite each other every frame.
