# Verify: Terrain System

Plan: [[Terrain_System]] (moved here at P10). Code note: `Memories/Code/Terrain.md`. Everything below is owner-only; the headless tests are green but nobody has looked at it.

## Edit-mode passes (Terrain Editor window, `Stitch Punk/Terrain`)
- [ ] Open the project: all nine `Tile_*` subscenes open on their own within ~10 s and the ground shows in the Scene view. (An open tile may draw flat in authoring mode; judge height in Play.)
- [ ] Tiles panel: hover an open tile and see "N / 600 decals+props, mask, verts". Push one tile past 600 markers and see the red "Over budget" line.
- [ ] Height: sculpt a hill across a tile border; `Verify Seams` reports 0 failing pairs.
- [ ] Paint: paint grass/forest/stone/dirt across a border; no visible seam, `Verify Seams` clean.
- [ ] Roads, rivers/lakes, decals, scatter: each panel draws, undoes, and survives a recompile.
- [ ] `Report Data Size`: PASS. Last run: 10.5 MB disk over 9 tiles, ~15 MB resident. The 1.2 MB tile budget is close (authoring data is 1.1 MB per tile); recheck on a baked build.

## Play passes
- [ ] Walk the player across three tiles: no stutter, falling, or sticking; no console spam.
- [ ] Units chasing across a border keep chasing.
- [ ] F9 test route: close the trade letter, keep the Game view focused, stand near abstract citizens, press F9; the citizen walks the road and you meet it along the way. Speed is on the `TerrainDebugRoute` object.
- [ ] Stand still while a route walks into the loaded ring from beyond the edge.
- [ ] Tile load hitch (P10-2 target < 5 ms main-thread per frame): the 2026-10-31 spike measured a 30 ms worst frame in the Editor for one empty tile reload. Not re-measured with decals/props; profile a ring load while walking.
- [ ] Caravan: drive up a terrain hill. It should roll over the slope, and when it is blocked it should still be able to pivot away (fixed 2026-11-02, untested).
- [ ] Lighting bridge: add `TerrainLightingBridge` to a scene object, use the "Test Lightning" context menu; the ground flashes once. With no bridge the ground looks as before.

## Decisions left open
- P10-5 ribbon roads: only if the splat + decal roads look wrong to you.
- Day/night cycle: nothing drives the sun or ambient; the bridge only exposes the terrain tint and the lightning flash.
