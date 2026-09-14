# Verify: A91 player build check

> **Status (2026-09-13):** accepted as "works for now". Not confirmed by a real build yet.

## What A91 should do

A player build stops if any actor profile has an animation name error (an entry with no animation
name, or a name missing from the Animation Names registry). The console shows:

> Player build stopped: 1 animation name error(s) in actor profiles. Fix each profile, or turn off
> 'Fail player builds on profile name errors' in Project Settings > DOTS Animation Toolkit > Animation Names.

It is proven in code (the hook throws when called directly), never through a real build.

## Why it is still unconfirmed

The build on 2026-09-13 failed on unrelated script errors, so the A91 message never appeared. The
errors are all in `Assets/_Scripts/Editor/` (`PropertyDrawer`, `Editor`, `IMGUI`, `AdvancedDropdown`,
`DotsAnimationToolkit.Editor` not found).

Likely cause: `Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so the
editor-only assembly is compiled into the player. The usual fix is `"includePlatforms": ["Editor"]`.
That is a game-side change and has not been made.

## Check it once builds compile

- [ ] Fix the player compile errors above.
- [ ] Make a broken profile: an actor profile with one animation entry and no name. Or ask Claude to
      recreate `Assets/A91BuildTest/A91BuildTest.profile.asset`; the recipe is in the A91 spec's §7.
- [ ] File ▸ Build Profiles ▸ Build, with an output folder outside the repo. Expect the message above
      and no build.
- [ ] Optional: untick **Fail player builds on profile name errors** and build again. It should get
      past that point. Tick it back on.
- [ ] Delete the broken profile.
- [ ] Tell Claude the result, so the A91 spec and roadmap drop the "works for now" note.
