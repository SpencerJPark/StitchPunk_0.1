# Skills

Project skills live in `.claude/skills/` and are git-synced with the repo.

**Do not maintain an index here.** Claude Code injects every skill's name and description
into each session automatically, so a list in this file is duplication that silently goes
stale — it previously advertised three skills that no longer exist, and reference files
that had been deleted.

To see what exists: `ls .claude/skills/`. Reference a scaffolder by name in a plan doc
under a **`Skills Needed`** heading so the right one is used at build time.
