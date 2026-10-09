# dashboard-layout Specification

## Purpose
Stores which dashboard widgets each user shows and in what order, so the layout survives reloads and devices.

## Requirements

### Requirement: Fixed set of widgets
The dashboard layout SHALL always consist of exactly these widgets: closing soon, upcoming interviews, offers to
answer, stale listings, pipeline, applied this week, to analyze, favorited listings, favorited companies.

#### Scenario: Layout lists every widget
- **WHEN** the user queries `dashboardLayout`
- **THEN** the result contains each of the nine widgets exactly once

### Requirement: Default layout
A user who has never saved a layout SHALL see all widgets shown, in the order listed above.

#### Scenario: Never saved
- **WHEN** a user with no saved layout queries `dashboardLayout`
- **THEN** all nine widgets are returned with `isShown: true`, in the default order

### Requirement: Save the layout
`saveDashboardLayout` SHALL accept a list of `{widget, isShown}`, store it as the user's layout with the list order as
the display order, and return the saved layout. Saving again SHALL replace the previous layout.

#### Scenario: Reorder and hide
- **WHEN** the user saves the widgets in a new order with one `isShown: false`
- **THEN** the returned layout and later `dashboardLayout` queries have that order and that widget hidden

#### Scenario: Save twice
- **WHEN** the user saves a second layout after a first
- **THEN** only the second layout is returned

### Requirement: Hidden widgets keep their place
A hidden widget SHALL stay in the layout at its saved position, so showing it again restores its place.

#### Scenario: Hide then show
- **WHEN** the user hides the third widget, saves, then saves the same order with it shown
- **THEN** it is returned at the third position with `isShown: true`

### Requirement: Layout must be complete
`saveDashboardLayout` SHALL fail with `InvalidDashboardLayoutError` and change nothing when the list is missing a
widget or contains one more than once.

#### Scenario: Missing widget
- **WHEN** the user saves a list of eight widgets
- **THEN** the request fails with `InvalidDashboardLayoutError` and the stored layout is unchanged

#### Scenario: Duplicate widget
- **WHEN** the user saves a list that repeats a widget
- **THEN** the request fails with `InvalidDashboardLayoutError`

### Requirement: Layouts are per user
Each user's layout SHALL be independent; one user SHALL NOT see or change another's.

#### Scenario: Two users
- **WHEN** user A saves a custom layout and user B queries `dashboardLayout`
- **THEN** B gets the default layout
