---
name: log-progress
description: Update docs/STATUS.md so it reflects what the project currently contains. Use after finishing a milestone or a notable feature, or when the user asks to log progress.
---

Update `docs/STATUS.md` cheaply and accurately:

1. Read `docs/STATUS.md` and note the `Last logged commit` hash.
2. Run `git log --oneline <hash>..HEAD` and `git diff --stat <hash>` (include uncommitted changes). If there's no hash yet, use `git log --oneline` and `git status --short`.
3. Read only the changed files that matter (endpoint maps, entities, options/config classes). Use Grep for `Map(Get|Post|Put|Delete)` and `DbSet<` rather than reading whole files.
4. Edit STATUS.md in place: update milestone statuses, endpoints, entities, config keys, and "Next up". Add at most a one-line entry to the Decisions log for each real decision made.
5. Set `Last logged commit` to the current `HEAD` short hash and `Updated` to today's date.

Rules: replace stale content rather than appending history. Keep the file under ~150 lines, with facts only and no prose. Don't commit.
