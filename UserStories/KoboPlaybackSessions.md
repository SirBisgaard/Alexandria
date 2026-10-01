# Kobo Playback Sessions

## Story

As a user listening to audiobooks on a Kobo device,
Wants each listening session to show up as a single row in the book's activity log,
So that I can see when, how long and how far I listened instead of a stream of start, pause and player events.

## Technical

Kobo analytics events are stored unchanged as raw device JSON. Sessions are built when the activity log is read (`ToActivityEntries()` in `BookActivityEventExtensions`), so all history is grouped and device retries (same `EventId`) can never double-count.

A session is a run of player events (`Playback`, `OpenPlayerScreen`, `TableOfContents`, `PlaybackSpeedChange`) from the same device:
- Between a playback `Start` and its `Pause` there is no gap limit — the device sends nothing while audio plays.
- After a `Pause` the next player event must follow within 37 minutes, otherwise a new session starts.
- Any other event (another device, ABS rows, `MarkAsFinished`, …) ends the session.

A session with more than one event is shown as type `PlaybackSession`:
`Listened 3h 9m · 0.0% → 10.4% · session 3h 23m`
- Listened = sum of `SecondsListened` on the session's `Pause` events.
- Percentages = first and last `ProgressPercent` in the session.
- Session = wall-clock time from first to last event, pauses included.

The row time is the session end; single player events are shown as before.
Pause descriptions format the listened time readably (`listened 2h 31m 32s` instead of `listened 9092s`).

Events within the same second keep their insert order (`rowid` tie-breaker) so Start/Pause order is deterministic.

## Acceptance Criteria

- [x] A Start followed hours later by its Pause shows as one session
- [x] A resume within 37 minutes of a Pause extends the session
- [x] A resume more than 37 minutes after a Pause starts a new session
- [x] Another event type or another device in between splits sessions
- [x] A lone player event is shown unchanged
- [x] Listened seconds are shown as hours/minutes/seconds

## Dependencies

A Kobo device syncing analytics events through Alexandria for a local audiobook.
