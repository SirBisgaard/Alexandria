# Building and contributing

This page is for people who want to build Alexandria from source, run it locally, change it, or send a pull request.

- [Prerequisites and platforms](#prerequisites-and-platforms)
- [Build, test and run](#build-test-and-run)
- [Developing without a Kobo](#developing-without-a-kobo)
- [Running against a real Kobo](#running-against-a-real-kobo)
- [Where to start reading](#where-to-start-reading)
- [Solution layout](#solution-layout)
- [The frontend](#the-frontend)
- [Tests](#tests)
- [Conventions](#conventions)
- [Reverse-engineering the Kobo protocol](#reverse-engineering-the-kobo-protocol)
- [CI and releases](#ci-and-releases)
- [Deploying your own build to a NAS](#deploying-your-own-build-to-a-nas)
- [Sending a pull request](#sending-a-pull-request)

## Prerequisites and platforms

| Tool | Why | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Everything targets `net10.0` | The solution is `Alexandria.slnx`, which needs SDK 9.0.200+ / Visual Studio 17.13+ / Rider 2024.3+. There's no `global.json` |
| `ffmpeg` and `ffprobe` on `PATH` | Audiobook metadata, chapters and transcoding | Not bundled. `apt install ffmpeg`, `brew install ffmpeg`, … |
| `kepubify` | EPUB → KEPUB conversion | **Bundled**, nothing to install (see below) |
| ImageMagick | Covers and comic renditions | Comes from the `Magick.NET-Q8-AnyCPU` NuGet package with its native libraries. No system install needed |

| Platform | Status |
|---|---|
| Linux x86-64 | Supported. This is what CI and the Docker image use |
| macOS (Intel or Apple Silicon) | Works. The bundled kepubify is x86-64, so Apple Silicon needs Rosetta installed |
| Windows | Builds, but there's no Windows kepubify, so the app and the tests that convert EPUBs don't run natively. Use WSL |
| Linux arm64 | Not supported: no kepubify binary, and the image is amd64-only |

**About kepubify:** the binaries are in `Alexandria.DataAccess/Externals/`: `kepubify` (Linux x86-64) and `kepubify-darwin` (macOS x86-64). MSBuild copies the right one into the output folder as `kepubify`, and on Linux and macOS runs `chmod +x` on it after every build. Alexandria looks for it next to its own assemblies and refuses to start without it.

Other notable NuGet packages:

- `Microsoft.Data.Sqlite`: the database, with hand-written SQL and no ORM.
- `SharpCompress`: reading CBR (RAR) comics.
- `System.IO.Hashing`: rendition signatures.
- `MSTest` and `Microsoft.AspNetCore.Mvc.Testing`: tests.

## Build, test and run

```sh
dotnet build Alexandria.slnx
dotnet test Alexandria.Test/Alexandria.Test.csproj
dotnet run --project Alexandria.Api/Alexandria.Api.csproj
```

The `http` launch profile listens on `http://0.0.0.0:8080`. Open <http://localhost:8080> and sign in with `alexander` / `TheGreat`.

Running a subset of the tests: test namespaces are `Alexandria.Tests.*` (note the plural), one per category.

```sh
# One class
dotnet test Alexandria.Test/Alexandria.Test.csproj --filter "FullyQualifiedName~SyncResponseTests"

# One category
dotnet test Alexandria.Test/Alexandria.Test.csproj --filter "FullyQualifiedName~Alexandria.Tests.KoboProtocol"
dotnet test Alexandria.Test/Alexandria.Test.csproj --filter "FullyQualifiedName~Alexandria.Tests.Business"
dotnet test Alexandria.Test/Alexandria.Test.csproj --filter "FullyQualifiedName~Alexandria.Tests.Data"
dotnet test Alexandria.Test/Alexandria.Test.csproj --filter "FullyQualifiedName~Alexandria.Tests.Integration"
```

### Local configuration

`Alexandria.Api/appsettings.json` holds the defaults, and everything lives under `./App_Data` (gitignored):

```json
"AlexandriaSettings": {
  "PublicUrl": "http://localhost",
  "DataFolder": "./App_Data",
  "EbookFolder": "./App_Data/books/ebooks",
  "AudiobookFolder": "./App_Data/books/audiobooks",
  "M4bFileTreatment": "Nothing"
}
```

To point at your own library, create `Alexandria.Api/appsettings.Development.json`. It's gitignored, so machine-specific paths never get committed:

```json
{
  "AlexandriaSettings": {
    "PublicUrl": "http://192.168.1.20:8080",
    "EbookFolder": "/home/you/Books/Ebooks",
    "AudiobookFolder": "/home/you/Books/Audiobooks"
  }
}
```

Environment variables (`AlexandriaSettings__EbookFolder=…`) work too.

> **Be careful with your real library.** Alexandria writes to your files: metadata edits are embedded in them, `ConvertAtIndex` deletes M4Bs after converting them, a library sync replaces CBR files with CBZ, and file management renames files. During development, point it at a copy.

The local database ends up at `Alexandria.Api/App_Data/alexandria.db`. Delete `App_Data` to start over. Settings such as the UUID seed, `SyncLibraryAtBoot`, the sync batch size and the comic rendering options live in the database's `Settings` table, not in `appsettings.json`.

### Docker

```sh
./build.sh   # docker build -t alexandria -f Alexandria.Api/Dockerfile .
./run.sh     # runs that image on :8080. Edit the book paths in the script first
```

`Alexandria.Api/Dockerfile` is the production image (see [Running on a server](running-on-a-server.md)). The root `Dockerfile` is a toolbox image instead: `catthehacker/ubuntu:act-latest` (an image made for running CI workflows locally) plus .NET 10 and ffmpeg. GitHub Actions doesn't use it. It's handy for running the tests in a clean Linux environment, for example from macOS:

```sh
docker build -t alexandria-toolbox .
docker run --rm -v "$PWD":/src alexandria-toolbox dotnet test Alexandria.Test/Alexandria.Test.csproj
```

## Developing without a Kobo

Most work doesn't need a device:

1. **Write a `KoboProtocol` test first.** `KoboProtocolTestBase` runs the full app with `FakeKoboUpstreamHandler` standing in for `storeapi.kobo.com`, so nothing leaves your machine. Use `RegisterJson(method, pathPrefix, status, json)` to give the fake store canned answers. The recorded fixtures in `TestData/KoboProtocol/` show what real Kobo requests look like.
2. **Pretend to be a Kobo with `curl`.** Pair a device in the web UI, then send what a Kobo would. The first request binds the serial and model, exactly like a real device:

   ```sh
   KEY=<device key from More → Devices>
   curl -s "http://localhost:8080/api/kobo/$KEY/v1/library/sync" \
     -H 'x-kobo-deviceid: DEV-TEST-SERIAL' \
     -H 'User-Agent: Mozilla/5.0 (Linux; U; Android 2.0; en-us;) AppleWebKit/538.1 (KHTML, like Gecko) Version/4.0 Mobile Safari/538.1 (Kobo Touch 0390/4.45.23684)'
   ```

   `0390` is the model code (Libra Colour), which also decides the comic screen profile. Without a Kobo account, the store phase of the sync just returns nothing, and your local books come back as `NewEntitlement` items. Follow the `x-kobo-synctoken` response header to page through the rest. Use a different key and serial for each fake device, or the key locks.

## Running against a real Kobo

The Kobo has to be able to reach your machine:

1. Set `PublicUrl` to your machine's LAN address and port, e.g. `http://192.168.1.20:8080`. That address gets embedded in every download and cover URL the Kobo receives.
2. Pair a device in the web UI (**More → Devices → Pair New Device**) and copy the URL it shows.
3. On the Kobo, set `api_endpoint` to that URL in `.kobo/Kobo/Kobo eReader.conf` (see [How it works](how-it-works.md#connecting-a-kobo)).
4. Sync on the Kobo and watch the console.

To see everything the Kobo sends, turn on **Proxy traffic logging** (see [Reverse-engineering the Kobo protocol](#reverse-engineering-the-kobo-protocol)).

## Where to start reading

| You're working on | Start at |
|---|---|
| What the Kobo sees | `KoboDeviceController` → `KoboLibrarySyncService` → `KoboStoreEntityMapper` |
| Scanning and indexing files | `IndexerService.Index` → `FileContentManager` → the indexers in `DataAccess/Files/Books/` |
| The web UI | `wwwroot/index.html` and `wwwroot/scripts/router.js` |
| Background tasks | `Api/Sync/`: `ILibraryTaskBroker`, `LibraryTaskBackgroundService`, `SyncTask`, `FileManagementTask` |
| Startup and wiring | `Alexandria.Api/Program.cs` |

## Solution layout

```
Alexandria.Api          ASP.NET Core host: controllers, auth, filters, background tasks, wwwroot
Alexandria.Business     Domain: models, interfaces, services, Kobo proxy logic
Alexandria.DataAccess   SQLite repositories, file indexers/handlers, external tools
Alexandria.Test         MSTest suite
```

Dependencies go one way: **Api → Business, Api → DataAccess → Business**. Business knows nothing about SQLite, files or HTTP hosting.

| Where | What |
|---|---|
| `Api/Controllers/Kobo/` | The Kobo-facing proxy. `KoboDeviceController` has the intercepted endpoints, and `KoboController` is the catch-all passthrough |
| `Api/Controllers/Alexandria/` | The web UI's API under `/api/alexandria/...`. Everything inherits `AlexandriaControllerBase` (authorized) |
| `Api/Filters/LibraryETagFilter` | ETag/304 handling for the library endpoints |
| `Api/Sync/` | `ILibraryTaskBroker` and `LibraryTaskBackgroundService`: one library task (`SyncTask`, `FileManagementTask`) at a time, with live progress fan-out |
| `Business/Library/Services/` | Books, authors, series, collections, settings, indexing |
| `Business/KoboStoreProxy/Services/` | One service per Kobo concern: sync, content delivery, reading state, analytics, products, sync tokens, traffic logging |
| `DataAccess/Database/` | `SqliteConnectionProvider` (one WAL connection behind a semaphore), `DatabaseInitializer` (versioned migrations), repositories |
| `DataAccess/Files/` | Per-media-type indexers, media handlers, converters and renamers, plus `FileContentManager`, which dispatches between them |
| `DataAccess/Externals/` | `KepubifyWrapper`, `FfmpegWrapper`, and the EPUB/M4B/MP3/CBZ format handlers |

`Program.cs` wires everything up. Most services are transient. The core library repositories, the connection provider, settings, the sync token service and the task broker are singletons.

### Adding a new media type

1. Add a value to `MediaType` (`Business/Library/Models/MediaType.cs`), then go through every `switch` on `MediaType`. Around a dozen files do one, including `KoboStoreEntityMapper` (the entitlement shape the Kobo gets) and the frontend.
2. Implement `IFileContentIndexer` (discovery, metadata extraction, cover, derivative files, cleanup) and `IFileContentMediaHandler` (reading content, streaming, embedding metadata and covers back into the file).
3. Implement an `IFileRenamer`. Without one, file management fails for that type.
4. If the format should be converted before indexing (like CBR → CBZ or M4B → MP3 folder), add an `IFileConverter`.
5. Register them all in `Program.cs` next to the existing ones.

`FileContentManager` picks the first registered implementation whose `CanHandle(path)` returns true, so dispatch is by path, not a central extension list.

## The frontend

There's no build step and no npm: plain JavaScript with jQuery and Bootstrap 5, served from `Alexandria.Api/wwwroot/`.

- `index.html` is a single-page shell (top bar, `#view-root`, bottom nav) with one `<template>` per view.
- `scripts/router.js` maps hash routes (`#/books`, `#/authors/{uuid}`, …) to a template plus an init function, and puts exactly one view into `#view-root` at a time.
- Each view's init function sets the global `pageReload` so sort, filter and sync can refresh the current view.
- Shared modals are added to the page once at startup (`app.js`, `search.js`, `sync.js`, …) and use delegated `$(document).on(...)` handlers.
- `login.html` is a standalone page. The old per-page `.html` files are redirect stubs.
- Styles live in `wwwroot/styles/` (mainly `kobo.css`). The design follows the Kobo: serif type, square corners, no rounded cards and no gradients. If a change wouldn't look at home on a Kobo screen, it probably doesn't belong.

Edit, save, reload. No restart needed for static files.

**There are no frontend tests.** Check UI changes by hand, in light and dark mode and at phone width, and say in the pull request what you checked.

## Tests

The suite is MSTest with method-level parallelism (`MSTestSettings.cs`). [`Alexandria.Test/TEST_STRATEGY.md`](../Alexandria.Test/TEST_STRATEGY.md) has the detailed rules.

| Folder | Namespace | What it covers |
|---|---|---|
| `Business/` | `Alexandria.Tests.Business` | Services with hand-written fakes |
| `Data/` | `Alexandria.Tests.Data` | File handlers, wrappers and comics, against real fixture files |
| `Data/Database/` | `Alexandria.Tests.Data.Database` | Repositories against a temp SQLite database (`DatabaseTestBase`) |
| `Integration/` | `Alexandria.Tests.Integration` | The full app via `WebApplicationFactory<Program>` (`IntegrationTestBase`) |
| `KoboProtocol/` | `Alexandria.Tests.KoboProtocol` | Response-shape parity with what a real Kobo expects. **This is the most important category.** If these break, devices break |

Rules:

- **Test data must be synthetic or copyright-free.** The fixtures in `TestData/` are a CC0 *Peter Pan* EPUB, generated M4B/MP3 files and a generated cover. Comic pages are generated on the fly by `ComicFixtures`. Never commit real books, audiobooks, or captured traffic that hasn't been scrubbed.
- Fixtures are always copied into a per-test temp folder with `Fixtures.CopyEpubTo(...)`, `CopyM4BTo`, `CopyMp3To`, `CopyMp3AudiobookTo`, … and never used in place. Each test gets its own `alexandria-test-{Guid}` folder and SQLite database and deletes them in `[TestCleanup]`.
- No mocking framework. Fakes are small private classes inside the test file, backed by dictionaries, with a `Seed(...)` helper.
- Tests that generate UUIDs call `GuidExtensions.SetNamespace(...)` in setup.
- Use the modern MSTest assertions (`Assert.HasCount`, `Assert.IsEmpty`, `Assert.StartsWith`, …).
- Naming: `{Method}_{Scenario}_{Expected}`. Kobo protocol tests start with the endpoint (`Kobo{Endpoint}_…`).
- Every public method on a registered interface should have a test that calls it directly.

Integration tests build a fresh `WebApplicationFactory` per test. `ConfigureTestServices` swaps the settings for temp paths and puts in a fast password hasher.

### Kobo protocol fixtures

`TestData/KoboProtocol/{endpoint-family}/NN.json` are recorded request/expected-response pairs. The tests compare **shapes** (keys and JSON types), not values, so they catch a missing or renamed field without being fragile.

They were extracted from real traffic with the `[Ignore]`d tool in `KoboProtocol/Tools/ExtractFixtures.cs`, which scrubs everything personal:

- auth headers and cookies are dropped
- device IDs and serial numbers become `TEST-SERIAL`
- access and refresh tokens, user keys, sync tokens and JWTs are replaced
- the device key becomes `aaaaaaaa-…`
- other UUIDs are remapped deterministically
- hosts become `test.example.com`
- titles, descriptions, publishers and ISBNs become placeholders
- `/v1/user/*` responses are dropped

**Always read the generated JSON before committing it.** The repository is public. Look in request *and* response bodies, not just headers.

## Conventions

Style isn't enforced by tooling: there's no `.editorconfig`, no analyzers, and CI doesn't check style. Reviews do.

### Code style

- .NET 10, nullable reference types, implicit usings, file-scoped namespaces, primary constructors.
- No `_` prefix on fields and no `Async` suffix on method names (`Index()`, not `IndexAsync()`).
- Every type declaration has a braced body, never a trailing `;`.
- Records declare `{ get; init; }` properties, never positional parameters.
- A single-statement `if`/`else`/`foreach` may skip braces, but the statement goes on its own line.
- Method signatures stay on one line.
- Update methods take the DTO. The controller stamps the route UUID onto it (`author.AuthorUuid = uuid; service.UpdateAuthor(author)`).
- Settings are immutable records registered as singletons, not `IOptions<T>`.
- Document domain knowledge (Kobo protocol quirks, metadata rules) in `/// <summary>` XML docs on the method, not in inline `//` comments. Save inline comments for the rare line that needs a "why".
- Only wrappers that start a child process (`FfmpegWrapper`, `KepubifyWrapper`) catch, log and rethrow, so stderr and the exit code end up in the log. In-process code lets exceptions bubble up to the global logging.
- File access: reads open with `FileShare.ReadWrite` and writes with `FileShare.Read`. Never use `File.OpenRead` or `ZipFile.OpenRead`. Libraries live on network shares, and lock contention is real.
- Batch work (transcodes, tag writes, ffprobe fan-out) goes through `ParallelismPolicy.MaxDegree`. Unbounded parallelism saturated a NAS-mounted library in practice.
- Never log secrets: the UUID seed, tokens, device keys, or Audiobookshelf tokens.

### Database migrations

`DatabaseInitializer` runs versioned migrations (`V1`–`V5`) at startup, and each database records the version it's at. A migration only runs on databases below its version.

**Every schema change is a new migration (`V6`, `V7`, …).** Never edit a migration that has shipped in a tagged release. Existing installs have already run it and would never see the change. Migrations only go forward, so keep them additive where you can.

### User stories

Every feature has a markdown file in [`UserStories/`](../UserStories) with the same shape:

```markdown
# Feature name
## Story          — As a …, Wants …, So that …
## Technical      — implementation notes
## Acceptance Criteria — checkbox list
## Dependencies
```

Read the relevant story before changing a feature, and add a story when you add a feature.

### Commits

Conventional commits: `type(scope): summary`, in lowercase imperative.

```
feat(comics): sync CBZ/CBR comics to Kobo as fixed-layout EPUBs
fix(kobo-sync): push externally-changed reading states to devices
```

The types are `feat`, `fix`, `refactor`, `test`, `docs`, `style` and `chore`. Scopes name the area, for example `kobo`, `kobo-sync`, `comics`, `integrations`, `events`, `api`, `db`, `frontend` or `library`. Explain *why* in the body. Design reasoning belongs in the commit message, not in code comments.

## Reverse-engineering the Kobo protocol

Most of Alexandria exists because the Kobo's traffic was recorded and studied. To add support for something new:

1. Turn on **More → Settings → General → Proxy traffic logging**. Every `/api/kobo/...` request and response (method, URL, headers, request and response bodies with gzip decoded, status, duration) is written to the `KoboTrafficLogs` table. Binary response bodies are stored as `BINARY`.
2. Do the thing on a Kobo pointed at your instance.
3. Inspect the traffic from the repository root:

   ```sh
   sqlite3 -readonly Alexandria.Api/App_Data/alexandria.db \
     "select Method, Url, StatusCode from KoboTrafficLogs order by Id desc limit 50"
   ```

4. Add an explicit route in `KoboDeviceController`. Anything without a route keeps falling through to the passthrough proxy, so the store keeps working.
5. Capture fixtures with `ExtractFixtures`: remove `[Ignore]`, run `dotnet test --filter FullyQualifiedName~ExtractFixtures` with `ALEXANDRIA_PROD_DB_PATH` pointing at the database, and put `[Ignore]` back. A new endpoint family usually also needs a branch in its `Classify` and `LimitFor` methods. Review the JSON, then add shape tests.

> **The traffic log is full of secrets.** It stores everything unredacted: your Kobo account's `Authorization` header, the access and refresh tokens from `/v1/auth/*`, your profile (name, email) from `/v1/user/profile`, your purchases, and the device key in every URL. Never attach that database to an issue, and remember that backups taken while logging was on carry the same data. Turning logging off clears the table. Logging also buffers whole responses in memory, book downloads included, so expect higher RAM use while it's on.

## CI and releases

[`.github/workflows/build.yaml`](../.github/workflows/build.yaml):

| Trigger | What happens |
|---|---|
| Pull request | Build and test |
| Push to `master` | Build and test |
| Tag `YYYYMMDDnn` (e.g. `2026101901`) | Build and test, then publish `ghcr.io/sirbisgaard/alexandria:<tag>` and `:latest`, and create a GitHub release |

- Releases are tagged with the date plus a two-digit counter for that day (`01`, `02`, …). The maintainer tags releases, and `master` should always be releasable.
- The tag is passed into the Docker build as `VERSION` and becomes the app's informational version, which the **About** dialog shows. Local builds show a `vYYYYMMDD-HHmm` timestamp instead.
- Release notes are generated from merged pull requests, so **pull request titles must follow the commit convention**. They become the changelog.

## Deploying your own build to a NAS

If you run a fork or your own registry, CI can redeploy the app on your NAS after pushing the image. On recent TrueNAS releases (this was done on TrueNAS 26), the API is JSON-RPC over WebSocket (`wss://<nas>/api/current`), using [`truenas/api_client`](https://github.com/truenas/api_client):

```python
from truenas_api_client import Client

with Client("wss://nas.example.lan/api/current") as client:
    client.call("auth.login_ex", {"mechanism": "API_KEY_PLAIN", "username": "ci-deploy", "api_key": API_KEY})
    client.call("app.pull_images", "alexandria", {"redeploy": True}, job=True, timeout=900)
```

- Create a **dedicated non-root user** for CI with an API key limited to managing apps, and keep the key in CI secrets.
- Keep TLS verification on. If the NAS uses its own self-signed certificate, install that certificate on the runner rather than turning verification off.
- `job=True` with a generous timeout makes the CI step wait until the redeploy has finished, so a failed pull fails the pipeline.

Lessons from doing this:

- **CI runners often can't reach the NAS through your reverse proxy.** Jobs run in their own Docker networks, so traffic arrives from container IPs or hairpins through your WAN address. A proxy that only allows LAN clients silently drops those connections, which shows up as confusing WebSocket errors. Point CI straight at the NAS's own address and port instead of the public name.
- **Split-horizon DNS bites CI too.** If a name resolves differently inside and outside your network, pin the NAS's LAN address for the runner (e.g. an `/etc/hosts` entry) rather than relying on DNS.

## Sending a pull request

1. Open an issue first for anything bigger than a small fix, so we can agree on the direction. Alexandria is deliberately small. It manages a library for a Kobo and won't become a web reader or a browser audiobook player.
2. Write or update the user story.
3. Keep `dotnet test` green and add tests in the right category. Changes to the Kobo protocol need `KoboProtocol/` coverage.
4. Follow the conventions above, and give the pull request a conventional-commit title.
5. Describe what you tested on a real device, if anything, and for UI changes what you checked by hand. The Kobo is the final judge.
