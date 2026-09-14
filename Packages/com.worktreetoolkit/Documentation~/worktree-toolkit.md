# Worktree Toolkit

## 1. What it is

Worktree Toolkit runs multiple git worktrees against one Unity project so several branches of
work — each usually driven by an AI coding agent — can be built, gated, and reviewed without
each one needing its own full project checkout. One git command-line tool (`worktree.py`) does
every git operation; an Editor window and a Claude Code skill are built on top of it. Only the
current checkout ("the stage") normally has Library and packages materialized; other worktrees
are lightweight by default.

## 2. Status

**Built:**
- **The CLI** (`Tools~/worktree.py`): worktree creation, claiming, staging, gating, merging and
  removal, all as git operations with `--json` output.
- **The node window** (`Window ▸ Worktree Toolkit`): trunk and branch cards, greyed when not on
  stage, with put-on-stage, return, merge, remove and reveal actions.
- **The in-Editor gate broker:** it compiles a worktree commit on request, runs named EditMode
  fixtures, and restores trunk. Toggle it at `Tools ▸ Worktree Toolkit ▸ Gate Broker Enabled`.

**Not built yet:** own-Editor mode (per-worktree Library plus linked packages). PlayMode fixtures
are refused by the broker.

## 3. Requirements

- git ≥ 2.38
- Python ≥ 3.9 on `PATH` (found as `python3`, then `python`, then `py -3`)
- Claude Code, if you're using the `/worktree-run` workflow or the installed hooks/agent — the
  CLI itself has no Claude Code dependency

## 4. Concepts

**Stage** — the checkout your Unity Editor has open. Only commands run from the stage can move
it; a command run from inside a worktree that tries to move the stage is refused.

**Worktree** — a separate git worktree, one per spec/branch, usually under
`.claude/worktrees/<name>`. By default it holds tracked files only — no `Library`, no linked
packages — so it stays small and Unity never needs to open it directly.

**Parked** — a worktree left at a detached `HEAD` pointing at its own last commit while the stage
has its branch checked out elsewhere for review. Files in a parked worktree don't change and
nothing reimports; a parked worktree refuses `gate` and refuses new commits from its lead until
review ends.

**Lead status** — the state a spec's lead agent reports for itself: `building`, `gating`,
`ready`, `failed`, or `done`. Review is refused while a lead reports `building` or `gating`.

**Source-only vs. own-Editor mode** — source-only (the default) means the worktree has no
`Library` and no linked packages, so Unity can't open it as its own project; it exists purely so
git and Claude Code agents can work in it. Own-Editor mode (opt-in per worktree, not yet built)
seeds a `Library` and links in gitignored packages — e.g. a large gitignored package such as
Rive — so a machine with enough RAM can run a gate in that worktree's own Editor instead of
queuing on the stage's.

## 5. Setup

Run from the repository root, in the stage:

```
python Packages/com.worktreetoolkit/Tools~/worktree.py doctor
python Packages/com.worktreetoolkit/Tools~/worktree.py install-claude
python Packages/com.worktreetoolkit/Tools~/worktree.py adopt
```

- `doctor` checks git version, Python, hook installation, broker heartbeat, and stage
  cleanliness. It never refuses — it reports problems, it doesn't block on them.
- `install-claude` copies the `spec-lead` agent and `worktree-run` skill into the project's
  `.claude/` folder and adds the `WorktreeCreate`/`WorktreeRemove` hook entries to
  `.claude/settings.json` (idempotent — existing hooks are left alone).
- `adopt` registers any worktrees that already exist on disk (e.g. from before the toolkit was
  installed) and flags stale ones. It never deletes anything.

## 6. Command reference

Invoke every command as `python Packages/com.worktreetoolkit/Tools~/worktree.py <command>
[--json]`.

Exit codes: `0` ok · `1` usage error · `2` refused (a precondition failed; reason on stderr and
in the JSON body) · `3` no broker running · `4` timeout · `5` underlying git command failed.

| Command | Runs from | Purpose |
|---|---|---|
| `doctor` | anywhere | Check git/python versions, hooks, broker heartbeat, stage cleanliness |
| `list` | anywhere | Print the worktree graph (branches, ahead/behind, dirty state, lead status) |
| `create <id> [--from <branch>] [--mode source-only\|own-editor]` | stage | Create a worktree manually |
| `hook-create` / `hook-remove` | Claude Code hooks | Create/remove a worktree as a Claude Code hook callback |
| `claim <id> --spec <path> --lead-model <m> --worker-model <m>` | inside a worktree | Bind a spec and models to a worktree, rename its branch to `spec/<id>` |
| `status <id> <building\|gating\|ready\|failed\|done>` | inside a worktree | Report lead status |
| `gate [--edit-mode <fixture>...] [--play-mode <fixture>...] [--timeout 900]` | inside a worktree | Request a compile/test gate; refuses if the worktree has uncommitted changes |
| `review <id>` / `return` | stage | Put a worktree's branch on stage for review, or return the stage to trunk |
| `stash-stage` / `pop-stage` | stage | Stash or restore stage changes before/after a stage move |
| `stage-commit <sha> --holder <text>` / `restore-trunk` | stage | Detach the stage at a commit under `stage.lock` for a gate, then put trunk back (used by the broker, or by an orchestrator gating through MCP when no broker runs) |
| `merge <id>` | stage | Rebase the worktree onto trunk, then fast-forward merge |
| `yaml-merge-driver <path to UnityYAMLMerge>` | stage | Register Unity's YAML merge tool as a local git merge driver for scenes, prefabs and assets |
| `remove <id> [--keep-branch] [--force]` | stage | Remove a worktree; its branch is deleted only if merged into trunk |
| `adopt` | stage | Register pre-existing worktrees; never deletes |
| `install-claude` | stage | Install the skill, agent, and hook entries into `.claude/` |
| `config [--trunk <branch>] [--add-noise-glob <glob>]... [--remove-noise-glob <glob>]...` | stage to change, anywhere to read | Show or edit the trunk branch and the stage noise globs (paths Unity rewrites on its own that should not block a stage move) |

Commands marked *stage* return exit code `2` if run from inside a linked worktree — an isolated
spec lead can never move the stage out from under you.

## 7. Review and merge workflow

1. A spec lead works in its own worktree and commits its changes there.
2. The lead runs `gate` from inside the worktree to request a compile/test check. This refuses
   (exit `2`) if there are uncommitted changes.
3. When the lead reports `ready`, run `review <id>` from the stage. This parks the worktree
   (detaches its `HEAD`) and switches the stage to `spec/<id>` so you can look at the change in
   your own Editor.
4. If the stage has modified tracked files (after noise globs), the move is refused (exit `2`);
   use `stash-stage` first if you need to move anyway, and `pop-stage` after `return`. The Editor
   window, once built, will also refuse on a dirty open scene or while Play mode is running.
5. Fix anything you find directly on the stage and commit it there.
6. Run `return` to switch the stage back to trunk and re-attach the worktree to its branch.
7. Run `merge <id>` from the stage: the worktree's branch is rebased onto trunk, then
   fast-forward merged into trunk. Conflicts are handed back to the lead (or a fresh one).
8. Run `remove <id>` to delete the worktree once its branch has merged; pass `--keep-branch` to
   keep the branch around after removal.

## 8. Warnings

> **Never run `git worktree remove --force` by hand on a worktree with linked folders.** On git
> 2.43 for Windows this deletes the *contents* of every linked (junction/symlink) folder inside
> the worktree, not just the link — including a linked package your stage still depends on.
> Always remove through `worktree.py remove`, which unlinks junctions before touching the
> worktree.

> **Enable long paths on Windows** (`git config core.longpaths true`). Unity projects nest deeply;
> without it git fails checkout and removal past 260 characters. `doctor` reports
> `longPathsEnabled`.

> **Unity writes files on its own** — `EditorBuildSettings.asset`, `SceneDependencyCache`, the
> anim-event registry, and similar. These count as dirty tracked files. Without configuring
> noise globs to ignore them, stage moves (`review`/`return`) will be refused far more often than
> expected.

> **Windows paths vs. Git Bash `/tmp` paths for agents.** An agent working through Git Bash sees
> `/tmp/...` as its own path space, but tools that read the filesystem directly (outside Git
> Bash) resolve that to `%TEMP%`/`C:\tmp`, not the worktree. Always give an agent the worktree's
> Windows absolute path rather than relying on a Bash-relative or `/tmp`-style path.
