# Design

## Context

See proposal.md for why. Today `Profile.Links` is a JSON column of `{Label, Url}`; `UpdateProfile` is a full replace.
`Profile.Location` is a nullable FK to the shared `Location` (SetNull). Experience and project skills are many-to-many
tags to `Skill` (`ProfileExperienceSkills`, `ProfileProjectSkills`); `ProfileSkill (ProfileId, SkillId, Level 1–5)` is
separate, so a tagged skill is not in the skills list unless added with `setProfileSkill`. Education and certification
have no skills.

## Goals / Non-Goals

**Goals:**
- Fields and rows shaped so a CV renderer reads them directly, and a future AI prompt builder can list fields to send.
- One consistent rule for entry tags vs. profile skills, with no new tables.

**Non-Goals:**
- Enforcing which fields an LLM sees. No LLM reads the profile yet; `docs/PLAN.md` §9 owns that feature.
- Syncing in both directions or tracking where a skill came from.

## Decisions

### Sensitive fields are plain nullable columns on `Profile`, flagged by a comment
`DateOfBirth` (`DateOnly?`), `Nationality`, `WorkAuthorization`. A comment on them says a CV prompt builder must use an
explicit field allowlist (like passing named props rather than spreading the whole object). A separate table or flag
would add structure with nothing reading it yet. Store the date, not an age: an age goes stale.

### Hobbies are a JSON `List<string>`
Leaf data, never filtered, joined, or matched (project rule). Same shape as `Links`. Replaced whole by `updateProfile`,
so order is the list order. Alternative, ordered rows like bullets, rejected: nothing needs a hobby id.

### Links keep the JSON column; `Kind` is an enum stored as a string
`ProfileLink { Kind, Label, Url }`. EF stores the enum as text, so the JSON stays readable and survives enum reordering.
Host check uses `Uri.Host` equal to the domain or ending in `.domain`. One link per non-`OTHER` kind, since a second
GitHub link has no meaning on a CV. `kind` is required on the input: a default of `OTHER` would let an old UI keep
working but hide the missing field; the UI follow-up is small. The migration rewrites the JSON to add `"Kind":"Other"`
with `json_set` per element (`json_each` + `json_group_array`), and leaves `Label`.

### References are rows, hand-ordered
`ProfileReference (Id, UserId, ProfileId, Name, Relationship, Company?, Email?, Phone?, Note?, Position)`, cascade from
the profile, `UserId` query filter, index `(ProfileId, Position)`. They hold a third party's personal data and a CV
renderer selects some of them, so they get ids. Position handling copies `ProfileBulletMutations` (append, renumber on
move/delete); separate mutations, not a shared generic, because there are only two users of the pattern and bullets
have the extra entry-kind lookup.

### Entry location is an optional FK to `Location`
Same as `Profile.LocationId`: `LocationLookup.FindOrCreateAsync`, `OnDelete(SetNull)`, on experience, education and
project. Certifications don't get one: an issuer's city isn't shown on a CV.

### Entry tags add to `ProfileSkill`, one way, level 1
After `FindOrCreateSkillsAsync` in the experience and project `ApplyAsync`, insert a `ProfileSkill` for each skill whose
id isn't already in the profile's skills. The level is mandatory (1–5) and an entry tag carries none; 1 is the
honest floor (a higher guess would put a false level on a CV), and `setProfileSkill` raises it. Alternatives:
make the level nullable (breaks the shared 1–5 scale with `ListingSkill`, and matching), or compute the list from tags
(loses levels). Untagging doesn't remove the profile skill, because it may be used elsewhere or have a level the user
set; and `removeProfileSkill` leaves tags, as today. The result: profile skills ⊇ tagged skills only at the time of
tagging, which is what the user sees as "the skills section is updated".

## Risks / Trade-offs

- [Level 1 shows up as a real level in matching] → Documented in the spec; the UI can show "unrated" later if needed.
- [`removeProfileSkill` then editing the entry re-adds the skill] → Expected: the entry still tags it.
- [Link JSON rewrite] → Test the migration on a copy of the DB with a link, an empty list, and a null-safe case.
- [Required `kind` breaks the current UI's `updateProfile`] → Flagged **BREAKING**; the UI session updates it.

## Migration Plan

`make migration name=AddProfileDetails` (backs up the DB). Hand-edit: add columns and table, rewrite `Links` JSON,
backfill `ProfileSkills`:

```sql
INSERT INTO ProfileSkills (ProfileId, SkillId, Level)
SELECT DISTINCT e.ProfileId, es.SkillId, 1 FROM ProfileExperienceSkills es JOIN ProfileExperiences e ON e.Id = es.ExperienceId
UNION SELECT DISTINCT p.ProfileId, ps.SkillId, 1 FROM ProfileProjectSkills ps JOIN ProfileProjects p ON p.Id = ps.ProjectId
EXCEPT SELECT ProfileId, SkillId, 1 FROM ProfileSkills;
```

Verify on a copy first. Rollback: restore the `*.db.bak`.
