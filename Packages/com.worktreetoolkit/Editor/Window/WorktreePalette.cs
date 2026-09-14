using UnityEngine;

namespace WorktreeToolkit.Editor
{
    // Mirrors the USS --worktree-color-* tokens in WorktreeToolkit.uss: Painter2D cannot read USS custom properties.
    public static class WorktreePalette
    {
        public static readonly Color Ground = new Color(22f / 255f, 22f / 255f, 22f / 255f, 1f);
        public static readonly Color Bar = new Color(8f / 255f, 8f / 255f, 8f / 255f, 1f);
        public static readonly Color Tile = new Color(44f / 255f, 46f / 255f, 45f / 255f, 1f);
        public static readonly Color TileLit = new Color(74f / 255f, 88f / 255f, 84f / 255f, 1f);
        public static readonly Color LineLocked = new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f);
        public static readonly Color LineLockedHover = new Color(90f / 255f, 90f / 255f, 90f / 255f, 1f);
        public static readonly Color Teal = new Color(32f / 255f, 190f / 255f, 158f / 255f, 1f);
        public static readonly Color TealHover = new Color(80f / 255f, 220f / 255f, 190f / 255f, 1f);
        public static readonly Color Crimson = new Color(200f / 255f, 32f / 255f, 30f / 255f, 1f);
        public static readonly Color Cream = new Color(240f / 255f, 234f / 255f, 214f / 255f, 1f);
        public static readonly Color CreamDim = new Color(240f / 255f, 234f / 255f, 214f / 255f, 0.45f);
        public static readonly Color Selection = new Color(245f / 255f, 245f / 255f, 245f / 255f, 1f);
        public static readonly Color Amber = new Color(232f / 255f, 170f / 255f, 60f / 255f, 1f);
    }
}
