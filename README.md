# Alexandria

## Introduction

Alexandria is named after the ancient Library of Alexandria — a place that gathered knowledge in one roof so readers did not have to chase it across the world. The project carries the same intent: one place for your books, one device to read them on.

It exists because no other project offered stable large-library management for a Kobo — ebooks and audiobooks, wirelessly and over the internet — in one place. Alexandria sits between the Kobo and the Kobo store, injects the personal library into the sync flow, and lets the device behave the way it already knows how to.

If you own your media and want it to just show up on your Kobo, this is for you.

## Features

### Kobo device features

- **Ebook sync.** Drop EPUBs into the library folder; they appear on the device alongside store purchases, with covers, series, authors, and reading progress preserved. Conversion to KEPUB happens automatically via the bundled `kepubify` so the Kobo's native reader treats them as first-class.
- **Audiobook sync.** Both M4B files and per-chapter MP3 folders are supported. Chapters, covers, and metadata are extracted and served to the device through the same sync flow the Kobo already understands.
- **Reading state round-trip.** Progress, finished-status, and bookmarks flow both ways between the device and Alexandria — start a book on one Kobo, pick it up on another.
- **Transparent pass-through.** Any Kobo endpoint Alexandria does not intercept is proxied straight to `storeapi.kobo.com`, so the store, firmware updates, and account features keep working.

### Frontend features

- **Media management.** A Kobo device inspired design, searching, and editing the library — books, authors, series, collections, and covers.
- **Metadata editing.** Edit titles, authors, series, publishers, contributors, and subjects; changes are embedded back into the source file and re-indexed, so the library file stays the source of truth.
- **Cover management.** Upload, replace, or remove covers; cache-busting is handled so the Kobo picks up the new art on the next sync.
- **Library sync control.** Trigger reindexing on demand and watch progress stream live.
- **Device and user administration.** Register Kobo devices, manage Alexandria user accounts, and tune application settings from the UI.

## Who this is for — and who it isn't

**It is for** people who enjoy owning their own media and enjoy spending time with their Kobo. The project is intentionally slim and focuses on one job: managing your media so the Kobo can enjoy it.

**It is not** going to become a web-based e-reader or a browser audiobook player. Reading and listening belong on the Kobo. Alexandria is the librarian, not the reading room.

## How to run

Alexandria is distributed as a Docker image — pull it from your registry of choice and run it with the bundled `compose.yaml`. The committed file uses placeholders; replace them with your own paths, registry, and public URL before running:

```yaml
services:
  alexandria:
    container_name: alexandria
    image: <registry>/alexandria:latest
    platform: linux/amd64
    ports:
      - 8080:8080
    user: 1000:1000
    privileged: false
    pull_policy: always
    restart: always
    volumes:
      - /path/to/your/ebooks:/books/ebooks
      - /path/to/your/audiobooks:/books/audiobooks
      - ./data:/data
    environment:
      # Logging
      - Logging__LogLevel__Default=Information
      # AlexandriaSettings
      - AlexandriaSettings__PublicUrl=https://alexandria.example.com
      - AlexandriaSettings__M4bFileTreatment=Nothing
      - AlexandriaSettings__DataFolder=/data
      - AlexandriaSettings__EbookFolder=/books/ebooks
      - AlexandriaSettings__AudiobookFolder=/books/audiobooks
```
Once the container is up, point your Kobo's DNS at the host running Alexandria and connect the device.

### Kobo eReader conf

A few settings need to change in the Kobo's config file.

Located at:

```
/.kobo/Kobo/Kobo eReader.conf
```

Update the API to use Alexandria as the source of books. Only change the `api_endpoint` — replace `<your-alexandria-server>` with the host where Alexandria is running, and `<device-uuid-token>` with the UUID token issued for this device.

```conf
[OneStoreServices]
account_page=https://www.kobo.com/account/settings
account_page_rakuten=https://my.rakuten.co.jp/
api_endpoint=https://<your-alexandria-server>/api/kobo/<device-uuid-token>
autocomplete=https://storeapi.kobo.com/v1/products/autocomplete
```

## Settings

A few settings are worth knowing about before you start filling up the library folder:

- **`PublicUrl`** — the URL your Kobo will reach Alexandria on. This gets embedded into sync responses (cover URLs, download URLs, manifests), so it must be what the device sees, not `localhost`.
- **`EbookFolder` / `AudiobookFolder`** — where Alexandria scans for media. EPUBs sit directly in `EbookFolder`. Audiobooks are either M4B files in `AudiobookFolder` or subdirectories of per-chapter MP3s.
- **`DataFolder`** — where the SQLite database, cover thumbnails, and generated derivatives (KEPUBs, transcoded chapters) live. Mount it as a volume; losing this folder means rebuilding the index.
- **`M4bFileTreatment`** — this is the one to think about before the first index. M4B audiobooks can be handled in three ways:
  - **`Nothing`** — chapters are transcoded from AAC to MP3 **on demand** when the Kobo asks for them. Cheapest on disk, but long chapters risk the device timing out mid-transfer.
  - **`TranscodeAtIndex`** — chapters are pre-transcoded to MP3 during indexing and cached. Costs more disk (you keep both the original M4B and the MP3 chapters) but transfers are reliable.
  - **`ConvertAtIndex`** — the entire M4B is converted to an MP3 folder at index time and the original M4B is deleted. Best compatibility with the Kobo, roughly the same disk footprint as the original file. This is the recommended choice if you don't need to keep the M4B around.

Settings can be seeded through `appsettings.json` (or the `AlexandriaSettings__*` environment variables shown in the compose file) and changed at runtime from the web UI.

## Contributions, Feedback & Ideas

Contributions are more than welcome — code, bug reports, ideas, feature suggestions, or general feedback. If you have something that would make Alexandria better for you, open an issue or a pull request. The project stays intentionally slim, but it grows in the direction of the vision.

## Thanks to external projects

Alexandria stands on the shoulders of a few excellent tools:

- **[FFmpeg](https://ffmpeg.org/)** — audio decoding, transcoding, and probing for every audiobook that passes through the indexer. Without it there is no audiobook support.
- **[kepubify](https://pgaskin.net/kepubify/)** — EPUB → KEPUB conversion so the Kobo's native reader gets a first-class experience for personal books. Bundled with the project.

And a special thanks to **Kobo** for making devices that are a genuine pleasure to read on, and for keeping their sync protocol approachable enough that a project like this is possible in the first place.

## AI Disclosure

Parts of this project were built with assistance from [Claude](https://claude.ai) (Anthropic).

Used for:

- Implementing draft work or cookiecutter-style boilerplate.
- Writing and polishing documentation.
- Helping reverse-engineer the Kobo sync protocol, enabling audiobooks.

## Copyright

Alexandria is released under the European Union Public Licence v. 1.2 (EUPL-1.2). See [LICENSE](./LICENSE) for the full text.
