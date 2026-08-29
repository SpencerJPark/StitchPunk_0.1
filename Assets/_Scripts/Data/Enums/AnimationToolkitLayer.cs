// The six-layer convention every rig in this game declares, in this order (Animation Toolkit
// Migration spec §4): Base/Action/Override/Face/Eyes/Mouth. Game code addresses toolkit playback
// layers by this enum rather than a raw byte so a typo is a compile error, not a silently wrong
// index — cast to byte at the AnimationCommandUtil/PlaybackQuery call site. Nothing in the toolkit
// enforces that layer 3 means "Face" on every rig; every RigAsset this game authors must declare
// this same six-layer list in this same order for a shared tag-bound clip set's starting-layer
// references to mean the same thing across rigs.
public enum AnimationToolkitLayer : byte
{
    Base = 0,
    Action = 1,
    Override = 2,
    Face = 3,
    Eyes = 4,
    Mouth = 5,
}
