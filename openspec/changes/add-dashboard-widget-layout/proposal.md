# Proposal

## Why

The UI dashboard has nine widgets, but their order and visibility are static. Users want to hide widgets they don't
use and reorder the rest, and have that survive reloads and other devices, so it has to be stored per user in the API.

## What Changes

- Add a per-user `DashboardWidget` row: widget key (fixed enum of the nine widgets), `IsShown`, `Position`.
- Add query `dashboardLayout` → all nine widgets in the user's order, each with `isShown`. A user who never saved gets the default order with everything shown.
- Add mutation `saveDashboardLayout(input: {widgets: [{widget, isShown}]})` → the saved layout. The list order is the new order; it replaces the whole layout. A list that is not exactly the nine widgets once each → `InvalidDashboardLayoutError`.
- Hidden widgets keep their position, so showing one again puts it back where it was.
- One migration creating the table (no seeding; rows appear on first save).
- Out of scope: the widget data. Each widget's content is built by the UI from the existing `listings` and `companies` queries.

## Capabilities

### New Capabilities
- `dashboard-layout`: which dashboard widgets a user shows and in what order.

### Modified Capabilities

## Impact

- New `Features/Dashboard` (`DashboardWidget`, `DashboardQueries`, `DashboardMutations`), `AppDbContext` query filter, one migration, `schema.graphql`, `docs/STATUS.md`, and tests.
- No new packages.
