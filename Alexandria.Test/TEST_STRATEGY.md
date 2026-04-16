# Test Strategy

This document defines the test types, conventions, and direction for the Alexandria test suite. It serves as a guide for what to test, how to test it, and what infrastructure each test category requires.

## Test Data Policy

All test data must be **synthetically generated** — no copyrighted ebooks, audiobooks, or cover images may be committed to the repository.

### Synthetic EPUB Generation

Tests that need EPUB files should generate them programmatically. A shared test helper should create minimal but structurally valid EPUBs with configurable metadata:

- Title, authors, language, publisher, publication date
- Series name and index
- Cover image (generated solid-color JPEG/PNG)
- Subject/category tags
- Contributor roles
- Multiple chapters with placeholder text

The generated EPUBs must be valid enough for `EpubFileHandler` to extract metadata, for `KepubifyWrapper` to convert, and for the Kobo device to accept as downloads.

### Synthetic M4B Generation

Tests that need M4B audiobooks should generate them using ffmpeg (available in CI). A shared test helper should produce minimal M4B files with:

- Embedded metadata tags (title, artist, album artist, grouping for series)
- Chapter markers
- Embedded cover image
- Minimal audio content (silence or tone, short duration)

The generated M4Bs must be valid enough for `M4BFileHandler` / ffprobe to parse metadata, chapters, and cover art.

### Synthetic MP3 Folder Audiobook Generation

Tests that need MP3 folder audiobooks should generate a directory of numbered MP3 files using ffmpeg. A shared test helper should produce a folder containing:

- Multiple short MP3 files (one per chapter), with ID3 track numbers for sort order
- Embedded ID3 tags on each file (title, artist, album, album artist, track number)
- Embedded cover image on at least the first file
- Minimal audio content (silence or tone, short duration per file)

The generated folder must be valid enough for `Mp3FileHandler` / ffprobe to extract per-file metadata, derive chapter structure from the file list, and extract cover art. The indexer discovers MP3 audiobooks as subdirectories of `AudiobookFolder` that contain `.mp3` files.

### Synthetic Cover Images

Cover images should be generated as solid-color JPEGs or PNGs at a known resolution (e.g. 800x1200). This ensures `CoverThumbnailService` can resize them and the Kobo device accepts them in cover responses.

### File Organization

Generated test data should be created in temporary directories during `[TestInitialize]` and cleaned up in `[TestCleanup]`. Shared generator helpers should live in a `TestData/Generators/` folder within the test project.

---

## Test Categories

### 1. Data Integration Tests

**Purpose:** Verify that metadata can be correctly extracted from and embedded into book files, and that the SQLite database correctly persists and retrieves all entity types.

**Scope:**

#### File Metadata

- Extract metadata from synthetic EPUBs and verify all fields (title, authors, series, cover, subjects, contributors, language, publisher, identifiers)
- Extract metadata from synthetic M4Bs and verify all fields (title, artist, album artist, narrator, series, chapters, duration, cover)
- Extract metadata from synthetic MP3 folder audiobooks and verify all fields (title, artist, album artist, series, chapters derived from file list, duration, cover, track ordering)
- Embed modified metadata into EPUB and re-extract to verify round-trip fidelity
- Embed modified metadata into M4B and re-extract to verify round-trip fidelity
- Embed modified metadata into MP3 folder audiobook (ID3 tags across files) and re-extract to verify round-trip fidelity
- Cover extraction and embedding for all three formats (EPUB, M4B, MP3)

#### Database

- Repository CRUD operations for each entity: Books, Authors, Series, Collections, ReadingStates, KoboDevices, KoboDeviceSyncs, Users, Settings
- Foreign key relationships: BookAuthors, BookSeries, CollectionBooks, AudiobookChapters
- Query operations: sorting, filtering, searching
- Schema migrations: `DatabaseInitializer` produces a valid schema from a fresh database
- Concurrent read/write behavior with SQLite (the application is single-writer but multi-reader)

**How they work:**

- Each test creates a temporary SQLite database via `SqliteConnectionProvider` + `DatabaseInitializer`
- File tests use synthetic EPUBs, M4Bs, and MP3 folders generated in a temp directory
- Tests exercise `EpubFileHandler`, `M4BFileHandler`, `Mp3FileHandler`, `CoverThumbnailService`, and the repository classes directly
- Temp directories and databases are cleaned up in `[TestCleanup]`

**Naming convention:** `{Entity}Repository_{Operation}_{ExpectedResult}` for database tests, `{Format}Metadata_{Operation}_{ExpectedResult}` for file tests.

---

### 2. Business Tests

**Purpose:** Verify the correctness of service-layer logic without external dependencies. These tests use fake repositories and fake file content handlers to isolate business rules.

**Scope:**

#### Indexing

- New files are indexed and persisted with correct metadata, UUIDs, and timestamps
- Re-indexing unchanged files is a no-op (CreatedAt and ModifiedAt preserved, fast path skips reprocessing when metadata + cover both match)
- Re-indexing changed files (different metadata) updates ModifiedAt but preserves CreatedAt and UUID
- Moved files (same metadata, different path) preserve the original BookUuid so reading state and device sync state are retained
- Stale entries (file deleted from disk) are removed from the database along with their derivatives (KEPUB, cover, pre-transcoded chapter cache)
- Non-existent or empty library folders produce no errors
- UUID v5 generation is deterministic for same title+authors
- `ReindexBook` preserves the original UUID after metadata edits
- `WordsPerPage` setting is respected when computing book statistics

#### M4B File Treatment Modes

The `M4BFileTreatment` setting controls how M4B audiobooks are processed during indexing. Business tests should cover each mode.

- **`Nothing`** — no derivatives created at index time. Chapter endpoint transcodes AAC to MP3 on demand via `FfmpegWrapper`. Verify that no files are written to `ChaptersFolder` during indexing
- **`TranscodeAtIndex`** — each chapter is pre-transcoded to MP3 during indexing and cached at `ChaptersFolder/{bookUuid}/{chapterIndex}.mp3`. Chapter transcoding is parallelized across chapters of a book. Verify that all expected MP3 files are created, the chapter endpoint serves from the cache (no on-demand ffmpeg process), and that cache files are cleaned up when a book is removed or reindexed
- **`ConvertAtIndex`** — the pre-index `FileMp3AudiobookConverter` converts each `.m4b` in `AudiobookFolder` to an MP3 folder (named `{Author} - {Series} #{Index} - {Title}`), then deletes the original M4B file. The resulting folder is indexed by `FileMp3AudiobookIndexer` on the same run. Verify that the M4B is deleted, the MP3 folder is created with correct naming, ID3 tags and cover are preserved, and the book is indexed as an MP3 folder audiobook

#### File Converter Plugins (`IFileConverter`)

Pre-index converters run before file discovery in `IndexerService.Index()`. They receive the current list of existing books and a cancellation token. The DI container registers converters as `IEnumerable<IFileConverter>` and the indexer invokes each in turn.

- Converter no-ops when its guarding setting is not active (e.g. `FileMp3AudiobookConverter` does nothing unless `M4BFileTreatment == ConvertAtIndex`)
- Cached derivatives keyed to a book being converted are preserved or migrated across the conversion
- Converter failure on one file does not block the rest of the indexing run
- Cancellation is respected promptly (the converter checks the token between files)

#### Book Service

- Archive/unarchive toggles `IsArchived` and updates ModifiedAt
- Reading status transitions: ReadyToRead → Reading → Finished
- Cover get/put/delete lifecycle
- Metadata update flow (service → indexer → re-extract)
- Recommendation logic
- Publisher catalog

#### Author / Series / Collection Services

- Rename-merge: renaming an author/series to an existing name merges books under the target
- List and get operations with correct book population
- Sorting behavior via `AlexandriaLibraryHelper`

#### Settings

- Get/update application settings
- `SyncLibraryAtBoot` flag behavior
- `M4BFileTreatment` round-trips through the DB and surfaces to the API
- `WordsPerPage` default and override behavior
- Indexed timestamp tracking
- Proxy-log cleanup runs when `ProxyLoggingEnabled` is disabled

**How they work:**

- Services are constructed with fake repositories (in-memory dictionaries, seeded with test state)
- No file system or database access
- Tests verify return values and side effects on the fakes
- Each test is independent — no shared mutable state

**Naming convention:** `{Method}_{Scenario}_{ExpectedResult}`

---

### 3. Integration Tests

**Purpose:** Verify the full request-response cycle from HTTP endpoint through the service layer to the database and back. These tests exercise the real ASP.NET Core pipeline, DI container, middleware, and authentication.

**Scope:**

- **Books API:** `GET /api/alexandria/books`, `GET /api/alexandria/books/{uuid}`, metadata update, cover upload/download/delete, archive, reading status
- **Authors API:** List, get, update with rename-merge
- **Series API:** List, get, update with rename-merge
- **Collections API:** List, get
- **Settings API:** Get, update
- **Sync API:** Library reindex via SSE stream
- **Devices API:** CRUD operations on Kobo device registrations
- **Users API:** CRUD operations, authentication
- **Auth:** Login returns valid token, authenticated endpoints reject missing/invalid tokens, `[AllowAnonymous]` on login works

**How they work:**

- Use `WebApplicationFactory<Program>` to spin up the full ASP.NET Core host in-process
- Override configuration to use a temp directory for `DataFolder`, `EbookFolder`, `AudiobookFolder`
- Pre-seed the database with synthetic books via the indexer or direct repository calls
- Make HTTP requests via `HttpClient` from the factory
- Assert on HTTP status codes, response headers, and deserialized JSON bodies
- Each test class gets its own factory instance to avoid cross-test contamination

**What to verify per endpoint:**

- Correct HTTP status codes (200, 201, 400, 401, 404)
- Response content type (`application/json`, `image/jpeg`, `application/kepub+zip`, etc.)
- Response body structure matches the expected JSON shape
- Side effects are persisted (e.g. updating metadata → re-reading returns new values)
- Error responses have meaningful messages

**Naming convention:** `{Endpoint}_{Method}_{Scenario}_{ExpectedResult}`

---

### 4. Kobo Protocol Tests

**Purpose:** Verify that Alexandria's Kobo-facing endpoints return responses that are structurally and semantically compatible with what real Kobo devices expect. These tests are the safety net for the sync flow — any change to controller responses must not break the device protocol.

This is the most critical test category. The Kobo device is a black box with undocumented protocol expectations. We have reverse-engineered the protocol through proxy traffic logging. These tests encode that knowledge as executable specifications.

**Scope:**

#### Sync Response Structure (`/v1/library/sync`)

- Response is a JSON array of `KoboStoreSyncResultItem`
- New ebooks use `NewEntitlement` with `BookEntitlement` + `BookMetadata` keys
- New audiobooks use `NewEntitlement` with `AudiobookEntitlement` + `AudiobookMetadata` keys
- Changed/removed books use `ChangedEntitlement` with correct entitlement key per media type
- `ReadingState` is returned inside entitlements for initial sync
- Reading state updates use `ChangedReadingState` as a separate sync item (not inside `ChangedEntitlement`)
- Collection sync uses `NewTag` / `ChangedTag` / `DeletedTag`
- Pagination uses `x-kobo-sync: continue` header and `x-kobo-synctoken` for continuation
- `BookMetadata.DownloadUrls` contains correctly formatted URLs with valid `Format`, `Size`, and `Platform`
- `BookMetadata.CoverImageId` uses the `{uuid}--v{unix_seconds}` cache-busting format
- `BookMetadata.ContributorRoles` and `Contributors` are populated
- `SeriesInfo` includes `Name`, `Number`, `NumberFloat`, and `Id`
- Audiobook metadata includes `Duration` (in milliseconds)

#### Download Endpoints

- `GET /download/{uuid}/kepub` returns `application/kepub+zip` content type with valid KEPUB binary
- `GET /download/{uuid}/manifest` returns JSON matching `KoboStoreAudiobookManifest` structure
- Manifest `Spine` entries have correct `MediaType`, `Duration`, `Url`, `FileExtension`
- Manifest `Navigation` entries match chapter metadata from the database
- `GET /audiobook/{uuid}/chapter/{partId}` returns `audio/mpeg` content type for all audiobooks
- MP3 folder audiobook chapters are streamed directly from the source MP3 file (no transcode)
- M4B audiobook chapters under `Nothing` treatment are transcoded on demand from AAC to MP3 via libmp3lame (or remuxed if source is already MP3)
- M4B audiobook chapters under `TranscodeAtIndex` treatment are served from the pre-transcoded cache at `ChaptersFolder/{bookUuid}/{chapterIndex}.mp3` with no ffmpeg invocation on the request path
- M4B audiobook chapters under `ConvertAtIndex` treatment are served as MP3 folder audiobooks (the source M4B has been deleted at index time)

#### Cover Endpoint

- `GET /{uuid}/{width}/{height}/{greyscale}/image.jpg` returns JPEG image data
- The `--v{...}` suffix on the UUID is stripped before lookup
- Unknown UUIDs return a 307 redirect (proxy to Kobo CDN)

#### Initialization Endpoint

- `GET /v1/initialization` rewrites resource URLs to point at Alexandria's public URL
- Only specific resource keys are rewritten; others are passed through

#### Reading State Endpoint

- `GET /v1/library/{uuid}/state` returns `KoboStoreReadingState` JSON for local books
- `PUT /v1/library/{uuid}/state` persists reading state and returns the updated state
- Remote (Kobo store) books are proxied transparently

#### Product Endpoints

- `GET /v1/products/books/{uuid}` returns `KoboStoreEbookProduct` JSON for local books
- `GET /v1/products/audiobooks/{uuid}` returns product metadata JSON for local audiobooks
- `GET /v1/products/{uuids}/prices` returns `KoboStoreProductPrices` with price 0 for local books
- `GET /v1/products/books/series/{uuid}` returns series info for local series

#### Analytics Endpoint

- `POST /v1/analytics/event` processes reading progress events for local books
- Remote events are proxied to Kobo

**How they work:**

- Two complementary approaches:
  1. **Unit-level JSON validation:** Construct domain objects, pass them through `KoboStoreEntityMapper` and other mapping logic, serialize to JSON, and validate the shape/fields against known-good structures captured from proxy logs
  2. **HTTP-level protocol tests:** Use `WebApplicationFactory<Program>` with a seeded library. Make requests with the same headers a Kobo device sends (including `x-kobo-synctoken`, device key in the URL path, etc.). Assert on response status, headers, content type, and deserialized JSON structure
- Known-good JSON structures (captured from proxy logs of working sync sessions) should be stored as embedded resources or constants in the test class, used as the "expected" baseline for structural comparisons
- Structural comparison means: all required keys are present, value types match, enums contain valid values. Exact value equality is only checked for fields that must be deterministic (e.g. format strings, URL patterns)

**Naming convention:** `Kobo{Endpoint}_{Scenario}_{ExpectedBehavior}`

---

### 5. Performance Tests

**Purpose:** Measure and track the performance of critical operations on synthetic data. These tests are not run in CI by default — they are triggered manually or on a schedule to detect regressions.

**Scope:**

| Operation | What to Measure | Synthetic Data |
|-----------|----------------|----------------|
| Full index | Wall-clock time for indexing N books from empty database | Generate 50-200 synthetic EPUBs, M4Bs, and MP3 folders in a temp folder |
| Reindex (no changes) | Wall-clock time for re-indexing when nothing changed | Same library, second pass |
| Reindex (with changes) | Time to detect and re-process changed files | Modify metadata on a subset before re-indexing |
| Load metadata | Time to extract metadata from a single EPUB/M4B/MP3 folder | Generated files of varying sizes |
| Store metadata | Time to embed metadata into a single EPUB/M4B/MP3 folder | Generated files |
| Database read | Time to query books with all relationships loaded | Pre-seeded database with N books |
| Database write | Time to insert/update books with full metadata | Bulk inserts |
| API (full stack) | Round-trip time for key endpoints with a seeded library | `WebApplicationFactory` with generated library |
| Chapter transcode quality | AAC-to-MP3 transcode time and output size per libmp3lame `-q:a` level (0–9) | Synthetic M4B with a chapter of representative length |
| TranscodeAtIndex | Wall-clock time to pre-transcode all chapters of an M4B library | Generate N synthetic M4Bs with multiple chapters each |
| ConvertAtIndex | Wall-clock time to convert an M4B library into MP3 folders | Generate N synthetic M4Bs with multiple chapters each |
| M4BFileTreatment comparison | Side-by-side timing of first-run indexing under each treatment mode | Same synthetic M4B library indexed three times (one per mode) |

#### Chapter Transcode Quality Comparison

The audiobook chapter endpoint serves MP3 audio to Kobo devices. For M4B audiobooks with AAC audio, this requires transcoding via libmp3lame. For MP3 folder audiobooks or M4B files that already contain MP3 audio, the source is remuxed directly (`-c:a copy`) with no re-encoding.

The transcode quality test applies only to the AAC-to-MP3 path. All `-q:a` levels (0–9) re-encode — they decode AAC to PCM then encode to MP3. `-q:a 0` is highest quality (slowest, largest output), `-q:a 9` is lowest quality (fastest, smallest output). None are lossless; the difference is encode complexity. Production code uses `SelectLameQuality()` to pick a `-q:a` level based on the source AAC bitrate.

This test should transcode the same chapter at each quality level and report wall-clock time, output size, and effective bitrate in a comparison table. The results inform whether the `SelectLameQuality()` mapping gives a good trade-off between transcode speed and audio quality for on-demand chapter streaming.

**How they work:**

- All test data is synthetically generated at test setup — no dependency on production libraries
- Tests use `Stopwatch` for timing and output results via `Console.WriteLine` for CI capture
- Each test has a generous `[Timeout]` to prevent hangs but no strict performance thresholds — the goal is regression detection through trend observation, not hard pass/fail gates
- Performance helper classes (decorator pattern around real implementations) can instrument per-operation timings
- Tests are marked with `[TestCategory("Performance")]` so they can be filtered in/out of test runs

**Naming convention:** `Perf_{Operation}_{Scale}` (e.g. `Perf_FullIndex_200Books`, `Perf_DatabaseRead_1000Books`)

---

## Test Conventions

### Structure

- One `[TestClass]` per logical unit under test
- `[TestInitialize]` / `[TestCleanup]` for setup and teardown — no shared state between tests
- Temp directories use `Guid.NewGuid()` to prevent parallel test collisions
- Call `GuidExtensions.SetNamespace(...)` in setup for any test that involves UUID generation

### Assertions

Use MSTest 4 assertion methods. Prefer specific assertions over generic ones:

- `Assert.HasCount(expected, collection)` over `Assert.AreEqual(expected, collection.Count)`
- `Assert.IsEmpty(collection)` over `Assert.HasCount(0, collection)`
- `Assert.IsNotEmpty(collection)` over `Assert.IsTrue(collection.Any())`
- `Assert.StartsWith(expected, actual)` over `Assert.IsTrue(actual.StartsWith(expected))`
- `Assert.IsGreaterThan(threshold, actual)` over `Assert.IsTrue(actual > threshold)`
- `Assert.ThrowsExactlyAsync<T>(...)` for exception verification

### Test Doubles

- Hand-rolled fakes, no mocking frameworks
- Fakes are defined as `private` or `internal` classes inside the test file that uses them
- Fakes use `ConcurrentDictionary` or `Dictionary` for in-memory storage
- Fakes expose `Seed(...)` methods for pre-populating test state
- Fakes return `Task.FromResult` / `Task.CompletedTask` for async interface compliance

### Naming

All test methods follow: `{MethodOrFeature}_{Scenario}_{ExpectedResult}`

Examples:
- `Index_NewBooks_IndexesAll`
- `ArchivedBook_PreviouslySynced_SentAsRemoved`
- `KoboSync_MixedEbookAndAudiobook_CorrectPropertyNames`
- `Perf_FullIndex_200Books`

### Parallelization and Isolation

Tests run at method level (`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`). Every test must be fully independent — no shared mutable static state, no shared temp directories, no shared database files.

Each test that touches the file system or database must create its own isolated workspace:

- **Temp directory:** `Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid())` — guarantees no two parallel tests write to the same location
- **SQLite database:** Created inside the test's temp directory so each test gets its own `alexandria.db`
- **Derived files (covers, kepubs, chapters):** Written to subdirectories under the test's temp directory
- **Synthetic test data:** Generated into the test's temp directory, never into a shared location
- **Cleanup:** `[TestCleanup]` must delete the entire temp directory recursively

Never use a fixed or shared path for any writable resource. Two tests running in parallel must never contend on the same file.

---

## Folder Structure

```
Alexandria.Test/
├── MSTestSettings.cs
├── TestData/
│   └── Generators/           ← Synthetic test data generation helpers
│       ├── EpubGenerator.cs
│       ├── M4BGenerator.cs
│       ├── Mp3FolderGenerator.cs
│       └── CoverGenerator.cs
├── Data/                     ← Data integration tests
│   ├── EpubMetadataTests.cs
│   ├── M4BMetadataTests.cs
│   ├── Mp3MetadataTests.cs
│   ├── BookRepositoryTests.cs
│   ├── AuthorRepositoryTests.cs
│   ├── SeriesRepositoryTests.cs
│   └── ...
├── Business/                 ← Business logic tests
│   ├── IndexerServiceTests.cs
│   ├── IndexerM4BTreatmentTests.cs
│   ├── FileMp3AudiobookConverterTests.cs
│   ├── BookServiceTests.cs
│   ├── AuthorServiceTests.cs
│   ├── SeriesServiceTests.cs
│   ├── CollectionServiceTests.cs
│   └── SettingsServiceTests.cs
├── Integration/              ← Full API integration tests
│   ├── BooksApiTests.cs
│   ├── AuthApiTests.cs
│   ├── SyncApiTests.cs
│   └── ...
├── KoboProtocol/             ← Kobo device protocol tests
│   ├── SyncResponseTests.cs
│   ├── DownloadTests.cs
│   ├── CoverTests.cs
│   ├── ReadingStateTests.cs
│   ├── InitializationTests.cs
│   ├── ProductTests.cs
│   └── AnalyticsTests.cs
└── Performance/              ← Performance benchmarks
    ├── IndexingPerformanceTests.cs
    ├── MetadataPerformanceTests.cs
    ├── DatabasePerformanceTests.cs
    └── ApiPerformanceTests.cs
```

---

## Guidelines

- Every new service method should have business tests covering its happy path and meaningful edge cases.
- Bug fixes should include a test that reproduces the bug before the fix is applied.
- Kobo protocol tests should be updated whenever a controller response format changes — the proxy logs are the source of truth for what the device expects.
- Performance tests should be run before and after changes to indexing, metadata handling, or database queries to detect regressions.
- Prefer testing behavior over implementation — tests should not break when internal refactoring preserves the same external behavior.
