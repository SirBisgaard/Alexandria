# Mass file renamer

## Story

As a logged-in user of Alexandria,
I want to normalize the filenames of books already in my library against a configurable template,
So that my on-disk library has a consistent, predictable naming scheme without me having to rename each file by hand.

## Technical

A new **"Manage Files"** entry on the More page, placed between *Devices* and *Settings*, opens a wide modal that carries the whole feature.

The modal has three sections:

1. **Filename template editor** — text input pre-populated from the persisted `Settings.FileRenameTemplate` column. The template syntax supports literal text, the tokens `<Authors>`, `<Title>`, `<Serie>`, `<SerieIndex>`, and optional groups in `(...)` that drop out when any token inside resolves to an empty value. Multi-author books comma-join the authors (", "). `<SerieIndex>` is formatted with invariant culture and trailing zeros trimmed (e.g. `1`, `2.5`). Illegal filename characters are stripped; consecutive whitespace is collapsed; leading/trailing spaces, dashes, and dots are trimmed. Rendered base name is capped at 200 characters (the `<Title>` portion is truncated first). A **Save template** button validates the template and persists it through `PUT /api/alexandria/settings`; an invalid template returns 400 with an inline error.

2. **Preview (dry-run)** — a **Dry run** button posts to `/api/alexandria/file-management/dry-run` (body empty — template is read from Settings) and renders a plan with three tabs: *Operations* (books that will be renamed), *Skipped* (books already matching the template), and *Conflicts* (books whose rendered target path collides with another book's target, or with an existing non-book file on disk). Operations tab shows the old and new filenames; conflicts tab shows the target path and the number of books involved.

3. **Run** — a **Run** button is enabled only when the last preview produced operations and zero conflicts. It opens an SSE stream against `POST /api/alexandria/file-management`, advancing a progress bar as each book is renamed. On completion, it reports the counts (renamed / skipped / failed) and re-runs the dry-run so the tables reflect the new state.

### Execution model

Per-format rename primitives live behind `IFileRenamer` (`.epub`, `.m4b`, `.mp3audiobook`). Each implementation has a two-phase contract:

- `PlanRename(book, newBaseName) → FileRenameStep` is pure (no I/O) and computes the exact filesystem moves that would happen — for MP3-folder audiobooks that includes a list of chapter-file renames into canonical `NN - <Title>.mp3` form.
- `Execute(step)` performs the moves. Transactional per-step: on partial failure completed moves are rolled back so the filesystem is left at the prior state. Rollback of a *successfully completed* Execute (driven by a failed DB write that followed it) is done by the caller calling `Execute(step.Invert())`.

`FileManagementService` orchestrates the run: `ComputePlan` walks the library, renders each book's target via `FileNameTemplate.Render`, and detects collisions. `Run` first re-computes the plan (so conflicts that appeared after the last dry-run are caught), acquires an exclusive lease on the existing indexing-broker slot via `ILibraryOperationLock` (so indexing and rename cannot race on `BookFiles.SourcePath`), and executes each operation with per-book rollback. A single `IDatabasePersistence.Flush` commits the batch at the end.

### Storage

A new **V2 migration** adds `Settings.FileRenameTemplate TEXT NULL`. No data backfill; existing installs keep a `NULL` template until the user sets one. No other schema changes — `BookFiles.SourcePath` and `AudiobookChapters.FileName` are updated in place via a new `IBookRepository.UpdateSourcePath` that writes both tables in one SQLite transaction.

### Kobo device impact

Book UUIDs are derived from `title:authors`, not path, so renames don't change identity. Derivatives (KEPUB cache, cover thumbnails, transcoded chapter MP3s) are all UUID-keyed under `DataFolder`, so they remain valid across renames. Devices that downloaded the book before the rename keep their entitlement — the next sync sees no change.

A Kobo device that happens to be streaming `/v1/download/{uuid}` while a rename is in flight for the same book will see a stale path mid-stream and fail the download; the device's normal retry loop recovers on the next sync tick.

## Acceptance Criteria

- [ ] More page shows a **Manage Files** entry between Devices and Settings, opening a wide modal
- [ ] The template input is pre-populated from `Settings.FileRenameTemplate`; saving an invalid template returns 400 with an inline error and does not persist
- [ ] Saving a valid template persists it and makes it available to both the dry-run and run endpoints
- [ ] `POST /api/alexandria/file-management/dry-run` returns 400 when no template is configured
- [ ] `POST /api/alexandria/file-management/dry-run` returns a plan listing only books whose current filename differs from the rendered template (already-matching books appear in Skipped)
- [ ] Two books rendering to the same target path appear in Conflicts with both UUIDs
- [ ] A target path that exists on disk and does not belong to a book in the plan appears in Conflicts
- [ ] The modal's Run button is disabled until a dry-run produces operations and zero conflicts
- [ ] `POST /api/alexandria/file-management` streams SSE progress frames and a terminal completion frame with renamed / skipped / failed counts
- [ ] Running with conflicts present returns 409 in the SSE stream with the conflict list
- [ ] Running while a library sync is in progress returns 409 without moving any files
- [ ] Ebook and M4B audiobook filenames are renamed in place; KEPUB cache and cover thumbnail files are not touched
- [ ] MP3-folder audiobook: the folder is renamed, each chapter file inside is renamed to `NN - <Title>.mp3` preserving index order, and `AudiobookChapters.FileName` rows are updated
- [ ] `BookFiles.SourcePath` reflects the new path after a successful run
- [ ] A failure during the per-book DB write causes the filesystem rename to be reverted; the book is reported in the terminal frame's `failed` list
- [ ] Re-running the dry-run against the same template immediately after a successful run produces an empty operations list (idempotent)

## Dependencies

The user is authenticated and logged into Alexandria.
