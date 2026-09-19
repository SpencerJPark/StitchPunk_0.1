# The Health Tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Health tab.

One project-wide list of cross-asset problems: a clip that names a rig its
clip set was never baked for, a profile pointing at an unregistered
animation name, an event key nobody registered, a clip set that doesn't bind
cleanly to a profile's rig. These are problems that only show up once you
compare two or more assets against each other, so they cannot live as a
per-clip inspector warning. The Clip Editor's toolbar used to carry its own
error badge for bind problems — that badge is gone. Health now reports
everything it used to catch, plus everything else. The Actor Profiles panel
still shows its own badge for profile-specific rules; that one stays.

## Layout

- **A severity filter** — All · Errors · Warnings · Notes, each with a live
  count, for example "Errors 2" — sits in the toolbar; click an entry to
  narrow the list to that severity while you work through it. A search field
  on the right narrows the list by code, title or asset name. **Scan project**
  sits at the right of the bar; a status label reads the time of the last
  scan and how many findings it produced, or "not scanned yet" before the
  first scan.
- **Two panels** fill the rest of the tab. The left panel is the findings
  list: each row is two lines — a severity dot and a short title on the
  first line, the affected asset's name on the second, with the finding's
  code shown as a meta badge rather than part of the title. Rows carry no
  buttons; clicking a row selects it and drives the panel on the right.
  Scanning again keeps the same row selected when its code and asset still
  match a finding; otherwise the next row down is selected.
- **The detail panel** on the right shows the selected finding in full: the
  code and severity, the title, a wrapped explanation of what's wrong and
  why it matters at run time, an **Affected assets** section (a ping button
  per related asset, and an open button where the window that fixes it can
  be opened directly), and **How to fix** — one full-width button per
  available action, each with a one-line description underneath it.

## The tab label

The tab reads **Health (n)**, where `n` is the number of Error-severity
findings — stale or unbaked VAT bakes count toward `n` too. At zero the tab
just reads "Health". While `n > 0` the label is drawn in the toolkit's error
colour. This count is already correct the moment the Clip Editor window
opens: the Health panel is built and scanned at window creation, not the
first time you open the tab, so opening the window costs one scan up front.
The list also rescans on its own about half a second after any toolkit
asset changes on disk, and again after you commit an edit in the Clip
Editor — you don't have to press Scan project after every change to see the
count catch up.

## Codes

| Code | Title | What it means | Actions |
| --- | --- | --- | --- |
| H01 | Clip is in no clip set | No set registers this clip, so no actor can play it. | Locate; Delete clip… |
| H02 | Clip set lists missing clips | The set has empty slots; baking skips them and remaining indices shift. | Remove missing; Locate |
| H03 | Profile names an unregistered animation | Play-by-name fails silently for that name at run time. | Locate profile |
| H04 | Profile rig differs from its clip set's baked rig | VAT textures were baked for a different rig; the actor deforms wrongly. | Locate profile; Locate clip set |
| H05 | Rig is used by no profile | Probably a leftover rig. | Locate; Delete rig… |
| H06 | VAT bake is stale or not baked | Actors play old motion, or none, with no run-time error to catch it. | Rebake; Locate; Delete VAT textures… (only when a texture set exists) |
| H07 | Track tag is not registered | The track binds to nothing on every rig. | Locate clip |
| H08 | Event key is not registered | Markers fire a key no system names. | Locate clip |
| H09 | Stable id not saved | The id re-mints on next load and breaks anything referencing it. | Save |
| H10 | Clip poses nothing on this rig | None of the clip's tags exist on the set's rig. | Locate clip; Locate rig |
| H11 | Clip set doesn't bind to its profile's rig | One finding per bind-validation message, with that message's own code (for example V03) in the title and its text as the detail. | Locate clip; Locate profile |
| H12 | Shared clip binding problem | One finding per message from the shared-clip binding check. | Locate clip |

H11 runs the profile's normal bind validation for each actor profile against
its own clip sets and rig, and turns every message the validator produces
into one finding. It skips a few validator codes on purpose — the ones for
a stale bake and for unregistered tags or event keys — because H06, H07 and
H08 already report those; nothing shows twice. A clip set that no profile
uses has no rig to validate against, so H11 says nothing about it — H01 and
H05 already cover orphaned assets. H12 checks clips that are shared between
profiles or rigs, keyed by their bound target, independently of H11.

## Deletes

Delete is available where it makes unambiguous sense, and it is always
behind a confirmation dialog. The dialog names the asset's path and quotes
what still references it, pulled from the project's asset reference index;
Cancel does nothing.

- **Delete clip…** (H01) moves only the clip asset to the OS trash. It does
  not touch any set, cutscene or profile that still names the clip — the
  confirmation lists those so you know before you delete.
- **Delete rig…** (H05) moves only the rig asset to the trash. Any profile
  or track still pointing at it will break; the confirmation lists them.
- **Delete VAT textures…** (H06) only appears when a baked texture set
  actually exists — a set that was never baked has nothing to delete. It
  trashes the texture set asset, its part textures, and any runtime meshes
  that no other VAT texture set references; textures shared with another
  set are left alone. Afterward the clip set counts as unbaked again, so H06
  stays in the list, now offering only Rebake.

None of these deletes are undoable except by recovering the file from the
OS recycle bin. Deleting never saves any other asset in the project.

## Not checked

- Scenes and prefabs — the Health tab only scans assets.
- The camera data warning — that check only runs at play time, not from the
  Health tab.
