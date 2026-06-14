# Plan Template — DOTS system spec

Copy this skeleton into `Assets/_Vault/Tasks/Claude/Plans/<System>_System.md` and fill every section with **real type and file references** found while grounding in the codebase. Delete sections that genuinely don't apply (e.g. AI integration for a non-behavioural system). Replace all `<…>` placeholders. Use `← DECISION:` inline for sub-choices left to Spencer and mirror them in the closing checklist.

---

```markdown
# <System> System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "<braindump anchor>"

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../../Memories/Code/Skills.md)):
- `<skill-name>` — <what it scaffolds here> (§<n>)
- …

---

## 1. Purpose & v1 scope
<One paragraph: what the system does and how it's entered.>
**v1 handles:**
- <in-scope item>
**Out of v1:** <deferred items, with the reserved hook if any>.

## 2. Architecture
<The high-level shape. If ECS-decides / MonoBehaviour-bridges, state the split and what crosses the boundary. State which SystemGroup it runs in and why. Include a small ASCII flow diagram if it clarifies.>
**← DECISION:** <any architecture fork>.

## 3. Entry points
<Which of the two entry patterns, with the actual component(s).>
- **<One-shot / request>** — <component struct + fields, and which system reads it>.
- **<Persistent / looping>** — <component struct + lifecycle>.

## 4. Data model
<SO→Blob library pipeline if any (FooSO → FooLibrarySO → FooLibraryBlob, enum-indexed, PostBakingSystemGroup). Config vs runtime. Managed registry for non-Blob references.>

## 5. Systems
<Each new system: name, group, what it queries, what it writes. Reference SystemGroups.cs placement.>

## 6. MonoBehaviour bridge  *(only if managed Unity objects are involved)*
<The PersistentSingleton<T> manager: what it pools/owns, what it reads from ECS each frame, the entity→object map.>

## 7. Integration points
<Existing systems/components/assets it touches: animation, combat, save/GameSettings, narrative, camera, items, movement. Note any shared-asset extensions that ripple into baking/save.>

## 8. Proposed file manifest
**New:** <paths>
**Edited:** <paths + what changes>
**Assets:** <SOs, mixers, prefabs>

## 9. Build phases
1. <Data layer …>
2. <One path end-to-end …>
3. …

## 10. Verification
<How to test each phase end-to-end — Play DOTSTestScene, trigger, inspect. Observable success signal per phase. What only Spencer can verify in the Editor.>

## Open decisions (collected)
- [ ] §<n> — <decision>.
- [ ] …
```

---

## Notes on filling it in
- The **Skills Needed** relative link assumes the doc lives in `Tasks/Claude/Plans/`. Adjust the `../` depth if the location differs.
- Prefer naming **real** components/systems/files (from grounding) over inventing names — a plan that names `AttackRequest`, `SystemGroups.cs`, `GameSettings`, etc. is executable; a vague one is not.
- Keep it **scannable but executable**: enough detail to build from, not a novel. Describe a repeated pattern once and point to representative files rather than enumerating every file.
- After writing, register the doc in `Plans/README.md` (status `✅ spec ready`).
