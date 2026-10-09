# Proposal

## Why

The app tracks listings but knows nothing about the person applying. Without a profile (experience, skills,
languages, preferences) there's nothing to match a listing against and nothing to build a CV from, and both are on
the roadmap (CV ↔ job match, CV generation). PLAN §4 planned several separate structured CVs. One master profile
replaces that: the data is entered once, and tailored CVs come later as selections from it.

## What Changes

- New `Features/Profile` slice. One `Profile` per user, created by the startup seeder for every user (like the
  lookups), so `profile` never returns null.
- **Basics**: full name, headline, summary (Markdown), email, phone, home location (shared `Location` lookup), and
  links (label + URL). Saved with `updateProfile` (full replace of the basics only).
- **Preferences**: target job roles (shared `JobRole` lookup), work modes, employment types, minimum salary +
  currency, notice period in months. Saved with `updateProfilePreferences`. Target locations stay on the existing
  `Location.IsTarget` (`saveLocation`); the profile only exposes them as `targetLocations`.
- **Experience**: employer (free text, not a `Company`), title, start/end month (no end = current), Markdown
  description, highlights, and the skills used (shared `Skill` lookup, found or created by name).
- **Education**, **certifications**, and **projects** (projects also tag skills).
- **Skills**: a 1–5 level per skill on the shared `Skill` lookup (same scale as listings). Each skill also exposes
  `experienceMonths`, computed from the experience entries that tag it, with overlapping jobs counted once.
- **Languages**: CEFR level per language (same `Language`/`CefrLevel` enums as listings).
- Per-item mutations: `add/update/delete` for experience, education, certification, project; `set/remove` for skill
  and language. Typed errors: `InvalidProfileError`, `ProfileEntryNotFoundError`.
- Dates are month precision: any date is stored as the 1st of its month.
- One migration (`AddProfile`), no backfill beyond the seeded empty profile rows.
- PLAN.md: the M3 "Structured CVs" milestone becomes "Profile"; the `Cv` section in §4 is replaced.

Out of scope: LLM or PDF import, CV variants and CV generation, listing ↔ profile matching, the guest sandbox profile.

## Capabilities

### New Capabilities
- `user-profile`: the one profile per user, its basics, its job preferences, and the `profile` query.
- `profile-history`: experience, education, certification, and project entries.
- `profile-skills`: skill levels, languages, and computed months of experience per skill.

### Modified Capabilities

## Impact

- New: `Features/Profile` (entities, `ProfileQueries`, `ProfileMutations`), migration `AddProfile`, seeder change
  (empty profile per user), tests.
- Shared lookups gain rows from the profile: skills and job roles created here appear in `skills`/`jobRoles` with
  `listingCount: 0`. That's intended, since matching joins on the same ids.
- `schema.graphql`, `docs/STATUS.md`, `docs/PLAN.md` updated. No breaking schema changes, only additions.
- UI (separate repo): new profile page. Month inputs send `YYYY-MM-01`.
