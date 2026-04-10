# Plan: Wire up KoboStoreKillSwitch

## Context

The `KoboStoreKillSwitch` setting already exists in the database schema, `AppSettings` model, and API models — but nothing reads it at runtime. When enabled, it should prevent all Kobo store API calls (except initialization), so the device only sees local Alexandria books.

## Approach

Use the **single chokepoint** — `KoboStoreProxyService.Proxy()` is the only method that makes HTTP calls to `storeapi.kobo.com`. Putting the kill switch there covers all 15+ proxy call sites across 5 consumers automatically. Two edge cases need surgical fixes.

## Changes (3 files)

### 1. `Alexandria.Business/KoboStoreProxy/Services/KoboStoreProxyService.cs`

- Add `IAlexandriaSettingsService` to the primary constructor
- Add `using Alexandria.Business.Library.Interfaces;`
- At the top of `Proxy()`, before any HTTP call:
  - Check `appSettings.KoboStoreKillSwitch`
  - If enabled and path is NOT `/v1/initialization` → return `KoboProxyResponse` with status 200 and body `[]` (empty JSON array)
  - Log the blocked request

**Why `[]` not `{}`**: `LibrarySync` deserializes the proxy response as `List<KoboStoreSyncResultItem>`. An empty array deserializes cleanly to an empty list. An empty object would throw.

**Why init is exempt**: The device needs initialization to discover Alexandria's rewritten URLs. Without init, the device can't route requests through Alexandria at all.

### 2. `Alexandria.Business/KoboStoreProxy/Services/KoboContentDeliveryService.cs`

- Add `IAlexandriaSettingsService` to the primary constructor
- Add `using Alexandria.Business.Library.Interfaces;`
- In `CoverImage()`, when book is null (non-local): check kill switch before returning the 307 redirect to `cdn.kobo.com`. If enabled, return 404 instead.

**Why**: `CoverImage` doesn't use the proxy — it returns a 307 redirect directly, which would still send the device to Kobo. This is the only endpoint that contacts Kobo outside the proxy.

### 3. `Alexandria.Test/Sync/LibrarySyncTests.cs`

- Update `KoboStoreProxyService` constructor call (line 64) to include `FakeSettingsService` parameter

`FakeSettingsService` already returns `AppSettings` with `KoboStoreKillSwitch = false` (the default), so all existing tests continue passing unchanged.

## Files NOT modified

- **KoboLibrarySyncService** — `LibrarySync()` already handles empty proxy responses correctly: the `[]` body deserializes to an empty list, sync token falls back via `??`, no continuation detected. No code changes needed.
- **KoboProductService, KoboReadingStateService** — only proxy for non-local books; the killed `[]` response is acceptable.
- **KoboController (catch-all)** — goes through the proxy, handled automatically.
- **Program.cs** — `IAlexandriaSettingsService` is already registered in DI.

## Verification

```bash
dotnet build Alexandria.slnx
dotnet test Alexandria.Test/Alexandria.Test.csproj
```

Functional test: toggle `KoboStoreKillSwitch` via `PUT /api/alexandria/settings`, then sync a Kobo device and verify no calls reach `storeapi.kobo.com` (check logs for "KoboStoreKillSwitch: blocking proxy" messages). Init should still work.
