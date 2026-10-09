# Design

## Context

See proposal.md for motivation and `specs/dashboard-layout` for requirements. Per-user rows live in their own
`Features/<Feature>` folder with `UserId`, a cascade FK to `AppUser`, and a global query filter in `AppDbContext`
(see `Industry`). Enums are stored as strings by convention. Dashboard widgets are not a user-created lookup: the
set is fixed in code.

## Goals / Non-Goals

**Goals:**
- Persist order and visibility with the smallest model that keeps layouts queryable and extensible.

**Non-Goals:**
- Serving widget data, per-widget settings (e.g. "show 5 items"), custom or multiple dashboards.

## Decisions

**One row per (user, widget): `Widget` enum, `IsShown`, `Position`.**
Unique index (UserId, Widget). Matches "widget, show, order" and leaves room for per-widget settings as extra columns
later. Alternative: one JSON column holding the list on the user. Simpler to write, but it hides the layout from the
relational model and a new widget or setting means reshaping JSON.

**Widget key is a C# enum, not a lookup table.**
Each widget needs UI code anyway, so the set only changes with a release. A table would need seeding for nothing. The
enum is stored as a string, so reordering or inserting members is safe.

**Missing rows mean defaults; no seeding.**
`dashboardLayout` takes the stored rows ordered by `Position`, then appends any widgets without a row in enum order,
shown. A new widget in a later release shows up at the end for everyone without a data migration.

**Save is a full replace, in one `SaveChanges`.**
The UI sends the whole list after a drag or toggle, so the server validates it is exactly the enum set (no missing,
no duplicates), deletes the user's rows, and inserts rows with `Position` = list index. Alternative: per-widget
move/toggle mutations, which need position shifting and race with each other. Hidden widgets stay in the list, which
is what keeps their place.

**Query returns a plain list, not `IQueryable` with paging.**
Always nine items, assembled in memory from defaults plus rows. Paging/filtering would add nothing. This is a
deliberate exception to the "IQueryable" convention.

**Typed error `InvalidDashboardLayoutException` via `[Error<>]`.**
Same pattern as `InvalidCompanyException`; an incomplete list is an expected failure the UI can show.

## Risks / Trade-offs

- [Two tabs save different layouts] → Last save wins; fine for a single-user preference.
- [A widget removed from the enum later leaves rows that no longer parse] → Delete those rows in that release's migration.

## Migration Plan

1. `make migration name=AddDashboardWidgets`: creates `DashboardWidgets` with the unique index and user FK. No backfill.
2. Roll back by reverting the migration; users fall back to the default layout.
