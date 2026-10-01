# AudioBookShelf Activity Session Merge

## Story

As a user listening to audiobooks through AudioBookShelf,
Wants each listening session to show up as a single row in the book's activity log,
So that the log shows when and how long I listened instead of one row per sync tick.

## Technical

The AudioBookShelf integration runs every 5 minutes and records an `AbsProgressUpdated` event for each push or pull.
While listening, ABS progress moves every tick, so a single session used to produce dozens of rows.

When recording an `AbsProgressUpdated` event, load the newest activity row for the book across all devices and event types:
- If it is an `AbsProgressUpdated` row from `AudioBookShelfIntegration` and its `EventTimestamp` is at most 37 minutes before the new event, update that row: move `EventTimestamp` to the new event time and replace `Body`. `EventId` and `ReceivedAt` stay unchanged.
- Otherwise insert a new row with `EventTimestamp = ReceivedAt = event time`.

`ReceivedAt` therefore marks the session start and `EventTimestamp` the latest update, which is what the UI shows.
Session length is `EventTimestamp - ReceivedAt` and includes pauses; the percentage shows how far the listener got.

Event time is ABS's `lastUpdate` for pulls and the local reading progress `ModifiedAt` for pushes.

Body format: `Pulled "<title>" ← ABS at 37.8% · session 19m` (or `Pushed "<title>" → ABS at …`). Sessions under 5 minutes show `< 5m`; sessions of an hour or more show `1h 5m`.

Other event types (`AbsProgressCleared`, Kobo device events) are never merged, and any such event between two ABS updates starts a new session.

The library version token includes the sum of all activity `EventTimestamp` values so a merge changes the ETag even when another book has a newer event.

## Acceptance Criteria

- [x] Consecutive ABS pulls/pushes for the same book within 37 minutes produce a single row
- [x] A gap of more than 37 minutes starts a new row
- [x] An event from another device or of another type in between starts a new row
- [x] The merged row keeps its `EventId` and `ReceivedAt` (session start)
- [x] The row body shows the latest percentage and the session length
- [x] The book modal shows the updated row after a merge (ETag changes)

## Dependencies

An enabled AudioBookShelf integration for the user.
