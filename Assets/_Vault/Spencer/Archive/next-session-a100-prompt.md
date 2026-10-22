You are running **Amendment A100 — Stats tab** on the DOTS Animation Toolkit package (`Packages/com.dotsanimationtoolkit`,
version 0.52.0 after A101). Spec: `Assets/_Vault/Tasks/AnimationPackage/A100_StatsTab_Spec.md` — its status line says an older
version; it takes `0.53.0`, correct the line. Read the spec in full, the roadmap §3 protocol, then only what §3 names.

**Run it alone, on trunk**, one orchestrator, `worker` subagents (sonnet) for the edits. Its T0 needs Play mode on
`Assets/Scenes/SubScenes/DOTSTestScene.unity` — **only with the owner's word in this session**; if it is not given, do the greps
and static grounding, build everything that does not need the probe, and stop at the probe with a clear message.

**Since A101, every new editor control is built through `ToolkitChrome` / `ViewportFrameElement` / `CatalogSidebarElement` /
`PathPickerRowElement` and a `toolkit-*` class**; `EditorStyleConformanceTests` (`Conformance_I`) fails on any inline colour, opacity,
font, border or radius write in `Editor/` unless the line ends `// colour from data`. Read `Assets/_Vault/Memories/Code/AnimationToolkit.md`
"Chrome is shared (A101)" before briefing a worker, and put the recipe in every brief: column + titled pane header, subject bar for
pickers, one primary action, hints and a status footer through `ToolkitChrome`, no inline visual styles.

Open owner checkpoints from the overnight run (do not re-ask; they are in HANDOFF §4): A96F multi-slot rule, A97F tagged parts
behind a confirm, A99's three ragdoll questions, A101's D7/D11/D13. Never close the Unity Editor.
