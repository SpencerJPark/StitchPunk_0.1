# DOTS Animation Toolkit

A DOTS-native animation toolkit for Unity Entities projects: 2.5D cutout
transform tracks, flipbook sprite animation, and bone/vertex animation textures
(VAT), with blob-baked clip registries, Burst-compiled sampling, and an editor
clip pipeline.

- **Package id:** `com.stitchpunk.dotsanimationtoolkit`
- **Version:** 0.3.0 (pre-release)
- **Unity:** 6000.5 minimum
- **Root namespace:** `StitchPunk.AnimationToolkit`

## Current state

This version covers Phase C build steps C0 through C2: the package skeleton, the
runtime data and sampling layer, and the authoring layer — clip/rig/set
ScriptableObjects, stable identity, the validation rule set, and the
deterministic clip-registry builder that bakes a set into its blob. No systems,
bakers, shaders, editor windows, or samples are implemented yet; nothing here
drives an entity. Those land in build steps C3 through C8.

See `Documentation~/index.md` for details, installation notes, and how to run
the conformance tests. See `CHANGELOG.md` for exactly what each version
contains.

## License

Proprietary — see `LICENSE.md`. This package is not yet licensed for
redistribution.
