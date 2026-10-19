# Audiobooks

Kobo devices with Bluetooth have a built-in audiobook player, made for audiobooks bought in the Kobo store. Alexandria serves your own audiobooks to that same player, with the chapter list, covers and narrators, and with full tracking: progress, listening sessions and finished status flow back from the device.

- [Supported formats](#supported-formats)
- [How the Kobo plays an audiobook](#how-the-kobo-plays-an-audiobook)
- [M4B handling: Nothing, TranscodeAtIndex or ConvertAtIndex](#m4b-handling)
- [MP3 folder audiobooks](#mp3-folder-audiobooks)
- [Metadata and tags](#metadata-and-tags)
- [Progress and listening sessions](#progress-and-listening-sessions)
- [Troubleshooting](#troubleshooting)

## Supported formats

| Format | Where it goes | Notes |
|---|---|---|
| **M4B** (AAC with chapters) | Directly in the audiobook folder | Chapters come from the file's chapter markers |
| **MP3 folder** | One subfolder per audiobook in the audiobook folder, one `.mp3` per chapter | Served as-is, no conversion |

```
audiobooks/
├── H. G. Wells - The Time Machine.m4b
├── Jack London - White Fang/
│   ├── 01 - Chapter 1.mp3
│   ├── 02 - Chapter 2.mp3
│   └── cover.jpg
└── ...
```

Only the top level is scanned: `.m4b` files directly in the folder, and direct subfolders containing `.mp3` files.

## How the Kobo plays an audiobook

1. During sync, the audiobook is sent as an `AudiobookEntitlement` with download format `MANIFEST`.
2. The Kobo downloads the **manifest**: JSON with two parallel chapter lists. `Navigation` holds the chapter titles, and `Spine` holds each chapter's duration, bitrate and URL `…/audiobook/{id}/chapter/{n}`.
3. It downloads each chapter from that URL as **MP3** (`audio/mpeg`) and plays them in its own player.

The Kobo only plays MP3, so how M4B chapters become MP3 is the important choice.

## M4B handling

The **M4B file treatment** setting decides that. Set the initial value with `AlexandriaSettings__M4BFileTreatment` before the first start (the default is `Nothing`), and change it later under **More → Settings → Audiobooks**.

| Mode | What happens | Disk | Reliability | Your M4B |
|---|---|---|---|---|
| `Nothing` (default) | Each chapter is cut and converted to MP3 **while the Kobo downloads it** | Least | Long chapters can time out | Kept |
| `TranscodeAtIndex` | Every chapter is converted to MP3 **once, during indexing**, and cached in `{DataFolder}/chapters/` | M4B + MP3 copies | Reliable | Kept |
| `ConvertAtIndex` | The M4B is turned into an **MP3 folder** during indexing, and the M4B is **deleted** | About the same as before | Best | **Deleted** |

### Nothing

ffmpeg extracts the chapter's time range and streams it straight to the Kobo. MP3 audio inside the file is copied as-is. AAC is encoded with LAME at a quality picked from the source bitrate: about `-q:a 9` for 32 kbps sources up to `-q:a 2` above 160 kbps, so you don't spend bits the source doesn't have.

In testing, the Kobo gave up when a download didn't start within about a minute. Encoding a one- to three-hour chapter can take longer than that, so with very long chapters downloads may fail. Short chapters are fine. If you run Alexandria behind a reverse proxy, turn off response buffering and allow long timeouts (see [Reverse proxies](running-on-a-server.md#reverse-proxies)).

### TranscodeAtIndex

During indexing, every chapter is encoded to `{DataFolder}/chapters/{BookUuid}/{n}.mp3`, in parallel (capped at `min(8, 80 % of CPU cores)`). The Kobo then downloads finished files. If a cached chapter is missing, it falls back to on-the-fly conversion. This costs more disk space, but the M4B stays untouched.

### ConvertAtIndex

Before discovery, each M4B is split into a folder named with your filename template, holding `NN - Chapter title.mp3` files with full tags, track numbers and a `cover.jpg`. Encoding goes a notch higher than on-the-fly streaming (up to `-q:a 0` for sources above 256 kbps). After that the audiobook is an ordinary [MP3 folder](#mp3-folder-audiobooks), and the original M4B is removed.

Safety nets:

- Conversion writes into the new folder with a `.converting` marker file and a temporary `.source.m4b` copy, so an interrupted run (a crash or restart) is cleaned up and retried.
- If a finished folder with the target name already exists, the M4B is skipped and **not** deleted.
- Any `TranscodeAtIndex` chapter cache for the book is removed.
- An M4B that fails to convert is moved to `{AudiobookFolder}/alexandria-conversion-failed/` so it isn't retried forever. Fix it, move it back, and sync again.

**Which one should I pick?** If you don't need to keep M4B files, use `ConvertAtIndex`. If you do, use `TranscodeAtIndex` if you have the disk space, and `Nothing` if your chapters are short or disk space is tight.

## MP3 folder audiobooks

- One subfolder = one audiobook, and each `.mp3` is one chapter.
- **Chapter order** follows the ID3 track number (`1/28`, `2/28`, …), then the filename. Files without a track number go last.
- **Book details** (title, authors, narrators, series, …) come from the first file's tags. The folder name is the fallback title.
- **Chapter titles** come from each file's `title` tag, with the filename as fallback.
- **Cover:** `cover.jpg` in the folder, otherwise the first `.jpg` (copied to `cover.jpg`), otherwise the first file's embedded art.
- Chapters are served straight from disk, with no conversion.

A folder named `alexandria-conversion-failed` is ignored.

## Metadata and tags

M4B and MP3 tags map to book fields like this:

| Field | Tag |
|---|---|
| Title | `title` (M4B), `album` (MP3 folder). Fallback: the file or folder name |
| Authors | `artist`, otherwise `album_artist`, comma-separated |
| Narrators | `composer`, comma-separated |
| Series | `grouping` as `Series name #2`, or `SERIES` + `PART` |
| Description | `description`, otherwise `comment` |
| Genre | `genre` |
| Publisher, language, date, copyright | `publisher`, `language`, `date`, `copyright` |

Editing an audiobook in the web UI writes these tags back: into the M4B (remuxed through a temp file), or into **every** MP3 in the folder. Series are written as `grouping`. M4B files can't hold publisher and language through ffmpeg, so for M4Bs those two stay in Alexandria's database only.

The same title by the same author can exist as an ebook *and* an audiobook. Alexandria links them for the Kobo so it knows they're the same work.

## Progress and listening sessions

- The Kobo reports playback through analytics events: play, pause and position. Alexandria turns them into progress (percentage, status *Reading* or *Finished*) that every other Kobo receives on its next sync.
- Each book's **Events** tab shows listening as sessions: *Listened 47m · 23.0% → 41.0% · session 52m*. A pause followed by more than 37 minutes of silence ends a session.
- If you also listen on your phone, [Audiobookshelf sync](audiobookshelf.md) keeps progress in step both ways.

## Troubleshooting

| Problem | Fix |
|---|---|
| A chapter fails to download, or stalls | On-the-fly conversion is too slow for a long chapter. Switch to `TranscodeAtIndex` or `ConvertAtIndex`. Behind a reverse proxy, also check buffering and timeouts |
| An M4B disappeared | `ConvertAtIndex` replaced it with an MP3 folder of the same name, which is expected. If it's in `alexandria-conversion-failed/`, it couldn't be converted. Check `exceptions.log` |
| Chapters are in the wrong order | Give the MP3s track numbers (`1/12`, …) or zero-padded filenames (`01`, `02`, …) |
| The audiobook doesn't appear | It must be a `.m4b` directly in the audiobook folder, or a direct subfolder containing `.mp3` files. Deeper nesting isn't scanned |
| Random ffmpeg errors during indexing on a NAS | Storage is overloaded. The built-in parallelism cap normally prevents this. Check the share, and keep the data folder on local storage |
