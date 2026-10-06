# Tasks

## 1. Storage

- [x] 1.1 Add `Features/Dashboard/DashboardWidget.cs` (entity with `Widget` enum of the nine widgets in default order, `IsShown`, `Position`, config with user FK cascade and unique (UserId, Widget)), register `DbSet` and query filter in `AppDbContext`, run `make migration name=AddDashboardWidgets`; verify `dotnet build` passes and the migration only creates that table

## 2. Query and mutation

- [x] 2.1 Add `DashboardQueries.GetDashboardLayout` returning all widgets: stored rows by `Position`, then missing ones in enum order shown; verify new `DashboardTests` for never-saved default, and for another user's layout not leaking
- [x] 2.2 Add `DashboardMutations.SaveDashboardLayoutAsync` with `[Error<InvalidDashboardLayoutException>]`: validate exactly the nine widgets once each, replace the user's rows with `Position` = list index in one `SaveChanges`; verify tests for reorder + hide, save twice, hide-then-show keeps position, missing widget and duplicate widget → `InvalidDashboardLayoutError` with the stored layout unchanged
- [x] 2.3 Regenerate `schema.graphql`, update `docs/STATUS.md` (entity, query, mutation, migration name) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS lists `dashboardLayout` and `saveDashboardLayout`

## 3. Final check

- [x] 3.1 Run `dotnet test` and `openspec validate add-dashboard-widget-layout`; verify both pass and `schema.graphql` is current
