# The Health Tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Health tab.

One project-wide list of cross-asset problems: a clip that names a rig its
clip set was never baked for, a profile pointing at an unregistered
animation name, an event key nobody registered. These are problems that only
show up once you compare two or more assets against each other, so they
cannot live as a per-clip inspector warning. The per-clip validation badge
you already know from the Clip Editor stays exactly where it is — the Health
tab does not replace it, it covers the ground between assets that badge
cannot see.

---

## What the tab gives you

- **Scan** re-runs every rule against the project's clips, clip sets, rigs,
  profiles and VAT texture sets, and rebuilds the list. You do not have to
  press it after every edit — the tab also rescans on its own about half a
  second after any toolkit asset changes, so the list catches up shortly
  after you save.
- **Each row** shows a severity dot, the finding's code, and a one-line
  message. Some rows carry a **fix** button that applies the repair directly;
  most do not, because the fix is a judgment call only you can make. Every
  row carries a **locate** button that selects the asset and pings it in the
  Project window, so you can jump straight to the thing that needs attention.
- **Per-severity toggles** let you hide Notes, Warnings or Errors while you
  work through the list, and a **text filter** narrows rows by code or
  message.
- **Stale or unbaked VAT bakes are always listed first**, ahead of every
  error, regardless of severity — the actor built against a stale bake still
  plays, it just silently plays old motion with no run-time error to catch
  it, so the list puts it where you cannot miss it.

## Codes

| Code | Severity | Means | Fix |
| --- | --- | --- | --- |
| H01 | Warning | A clip is in no clip set. | none |
| H02 | Error | A clip set lists a clip that no longer exists. | Remove missing. |
| H03 | Error | A profile names an animation that is not in the Animation Names registry. | none |
| H04 | Error | A profile's rig differs from the rig a listed clip set's VAT textures were baked for. | none |
| H05 | Note | A rig that no profile uses. | none |
| H06 | Error | A VAT texture set is stale or unbaked. The row names the set, its rig and the reason; Locate pings the texture set, then the clip set behind it. | Rebake (opens VAT Bake with the set and its rig). |
| H07 | Error | A clip track's tag is not in the Target Tags registry. | none |
| H08 | Error | An event key used by clips is not in the Event Keys registry. | none |
| H09 | Warning | An asset's stable id was never saved, so it would re-mint on the next load. | Save (saves that one asset). |
| H10 | Warning | A clip in a profile's clip set uses only tags that profile's rig has no target for, so the clip poses nothing on that rig. | none |

## Not checked

- Scenes and prefabs — the Health tab only scans assets.
- The camera data warning — that check only runs at play time, not from the
  Health tab.
