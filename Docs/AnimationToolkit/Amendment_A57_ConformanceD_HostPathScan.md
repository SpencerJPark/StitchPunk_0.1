# Amendment A57 — Conformance_D cannot tell the host's asset paths from Unity's

**Raised:** 2026-08-29, during the first green gate of Phase F / A55 / A56.
**Status:** open — needs an owner decision. Nothing has been changed on either side.

## 1. What happened

`PackagingConformanceTests.Conformance_D_NoHostNamespaceOrHostAssetPathReferences` is the only
red test left in the suite (EditMode 696/697; PlayMode 240/240). It reports:

```
Editor/ClipEditor/ClipEditorWindow.cs (host asset folder path)
Editor/Inspectors/VocabularyConstantsSection.cs (host asset folder path)
CHANGELOG.md (host asset folder path)
```

## 2. Why it fires

The rule scans every package text file for two regexes. The second is the literal `Assets/`:

```csharp
Regex hostAssetPathPattern = new Regex("Asse" + "ts/");
```

`Assets/` is not the host game's path. It is **every** Unity project's root content folder,
including the folder of whoever buys this package. The rule's stated intent (§(d): "No package file
references the host game at all, by name or by asset path") is right; its implementation cannot
distinguish `Assets/_Scripts/...` — a genuine leak of Stitch Punk — from `Assets/` used as the
generic destination any consumer would have.

The rule was written before the package generated anything into the consumer's project. A53/A54
changed that.

## 3. The one that is not cosmetic

`VocabularyConstantsSection.cs:47`:

```csharp
private const string DefaultDestinationDirectory = "Assets/Generated/DotsAnimationToolkit";
```

This is where the generated `TargetTags` / `AnimEvents` constants are written. It is functional, and
it collides head-on with the standing owner directive in HANDOFF §5:

> **"I shouldn't have to manually assign any assets for this."** Both vocabularies auto-create under
> `ProjectSettings/`. No asset creation, no wiring.

A default destination is what makes the feature require no wiring. Removing the constant to satisfy
Conformance_D would mean the consumer has to nominate a folder before constants can be generated —
trading a real feature for a lint rule.

The other three hits are prose and could be reworded away: a doc comment in `ClipEditorWindow.cs:741`,
a comment in `VocabularyConstantsSection.cs:158`, and a `CHANGELOG.md` line. Rewording them would
mask the problem rather than resolve it, and the constant would still fail.

## 4. Options

| # | Option | Cost |
|---|---|---|
| A | **Narrow the regex** to host-specific paths — match `Assets/` only when followed by a host folder segment (`_Scripts/`, `_Vault/`, `Scenes/`, `ScriptableObjects/`), leaving a bare `Assets/` legal. | Small. Keeps the rule's real intent, keeps the feature, keeps all four sites. Rule stops catching a host path under a folder name nobody listed. |
| B | **Exempt the generated-constants destination** by name, leave the regex alone, reword the three prose hits. | Smallest diff. Leaves the rule unable to express *why* that one string is allowed; the next generated-output feature hits this again. |
| C | **Make the destination consumer-supplied** with no default. | Satisfies the rule as written. Contradicts the HANDOFF §5 directive above; the owner would have to nominate a folder before generating constants. |

**Recommendation: A.** The rule exists to stop Stitch Punk's name and folders shipping to a buyer.
A bare `Assets/` does neither. Narrowing it keeps that guarantee while letting the package address
the consumer's project, which any package that generates code into a project has to do.

## 5. Not decided here

Per HANDOFF §6 this is escalated, not applied. The test and all four source sites are untouched, and
`Conformance_D` stays red until the option is chosen.
