# Tasks

## 1. External candidate validation

- [x] 1.1 Reject null items in every supported external candidate section array and nested bullet arrays with a typed, path-specific validation error; verify preview tests cover each shape and assert no draft is saved.
- [x] 1.2 Verify that each externally attributed bullet's normalized text matches its referenced profile bullet; add tests for altered-text rejection and successful preview of exact text.
- [x] 1.3 Keep user edits valid when saving source-attributed draft bullets; verify a save test accepts edited text with a valid source ID and preserves that text.

## 2. Integration verification

- [x] 2.1 Run the focused tailored-CV tests and full test suite; verify preview failures remain typed GraphQL validation errors, valid candidates remain review-only, and no schema or migration changes are introduced.
