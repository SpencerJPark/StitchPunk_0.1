using Unity.Entities;
using UnityEngine;

// Scene GameObject holding the single _ColorPaletteLibrary.asset. Bakes the reference + empty holder
// that ColorPaletteLibraryBakingSystem (PostBakingSystemGroup) fills with the baked blob. Mirrors
// PartLibraryAuthoring. The authoring never builds the blob itself — that split is what lets the blob
// rebuild when the SO changes during incremental baking.
public class ColorPaletteLibraryAuthoring : MonoBehaviour
{
    [Header("Library")]
    [Tooltip("ScriptableObject holding every ColorPaletteSO that should be baked into the ColorPaletteLibrary blob.")]
    public ColorPaletteLibrarySO library;

    public class Baker : Baker<ColorPaletteLibraryAuthoring>
    {
        public override void Bake(ColorPaletteLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new ColorPaletteLibraryReference { library = authoring.library });
            AddComponent(entity, new ColorPaletteLibrary());
        }
    }
}
