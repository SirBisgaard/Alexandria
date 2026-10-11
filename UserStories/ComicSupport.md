# Comic support (CBZ / CBR)

## Story

As a Kobo reader with comics in my library,
I want Alexandria to sync my CBZ and CBR files to my device as properly laid-out comics,
So that they open full-screen in the Kobo comic reader, sized and coloured for my screen, without converting them by hand.

## Technical

Kobo's store sync cannot deliver CBZ/CBR — Nickel only accepts EPUB-family `DownloadUrls[].Format` values, and the real store sends comics and manga as fixed-layout EPUB (`EPUB3FL`, seen in our own traffic log for One Piece and other manga). Alexandria therefore keeps the comic archive as the source of truth and builds a fixed-layout EPUB per device screen at index time, the same way M4B audiobooks are pre-transcoded under `TranscodeAtIndex`. There is no on-demand mode.

### Library files

- Comics live in `EbookFolder` alongside EPUBs. A new `MediaType.Comic` covers them.
- `FileCbrComicConverter` (an `IFileConverter`, runs before discovery) repacks every `.cbr` as a `.cbz` with the same base name and deletes the original — RAR cannot be written, and CBZ makes metadata editable. Solid archives are read in one forward pass, others entry by entry (SharpCompress). Failed conversions are quarantined in `alexandria-conversion-failed/`.
- `CbzFileHandler` reads metadata from `ComicInfo.xml` (Title, Series/Number, Writer → authors, Penciller/Inker/Colorist/Letterer/CoverArtist/Editor/Translator → contributors, Summary, Publisher, Year/Month/Day, LanguageISO, Genre, GTIN, Manga). Without ComicInfo the file name is parsed against the default rename template. Pages are image entries sorted naturally by path (backslash separators, `__MACOSX`, dot-files skipped). The cover is the `FrontCover` page from the Pages block, otherwise the first page; covers cannot be replaced.
- Edits are written back into `ComicInfo.xml` (unmanaged elements kept, schema order enforced) by rewriting the archive through a temp file. Fields ComicInfo cannot hold (Rights, Source, Type, Relation, Coverage, Format, Identifiers, colour mode) live only in the database.

### Device renditions

- `ComicDeviceProfile` maps the device model code (from the `Kobo Touch NNNN/` user agent captured at first handshake) to a screen size and colour capability — e.g. Libra Colour → `1264x1680-color`, Clara BW → `1072x1448-gray`.
- `FileComicIndexer.CreateDerivativeFiles` builds one rendition per distinct profile among registered devices at `{DataFolder}/comics/{profile}/{bookUuid}.{signature}.epub`. The signature hashes source size + mtime, reading direction, colour mode, `ComicEpubBuilder.BuilderVersion` (bumped when the rendering logic changes) and every comic rendering setting, so any change produces a new file and the stale one is removed.
- `ComicEpubBuilder` (pipeline modelled on Kindle Comic Converter): empty margins are cropped to the art (at most 10 % per side; only when the outer frame is one uniform tone, so full-bleed art is untouched; detached specks near the edge ignored; a coloured cover is never cropped); wide pages (double-page spreads) are rotated a quarter turn counter-clockwise so the right edge points up; pages larger than the screen are shrunk to fit (never enlarged), except that a page within 0.015 of the screen's height/width ratio is scaled to fill the screen and centre-cropped; colour is kept only on colour screens — per page in `Auto` mode (more than 0.1 % strongly coloured pixels), or forced by the book's colour mode; grayscale pages are auto-levelled unless deliberately low-contrast; JPEG quality 85. The OPF declares `rendition:layout` pre-paginated, `rendition:spread` landscape, `page-progression-direction` from the reading direction, and per-page `rendition:page-spread-*`.
- When a device with a not-yet-used screen profile completes its first handshake, a library sync is queued so its renditions are built.

### Sync and download

- Comics are sent as `BookEntitlement` with `DownloadUrls[0]` = `{ Format: "EPUB3FL", Url: …/download/{entitlement}/epub, Size: <this device's rendition size> }`.
- A comic whose rendition for the requesting device doesn't exist yet (unknown model, or the sync that builds it hasn't run) is held back and not recorded as synced, so it is offered again on the next sync.
- `/download/{entitlement}/epub` serves the rendition for the requesting device's profile.

### Settings page

The **Comics** tab exposes KCC-style choices (`ComicRenderingSettings`): double-page spreads (Rotate / Split / Split and rotate), margin cropping (on/off), cropping strength (Gentle / Normal / Aggressive), preserve margin %, image quality, and auto-contrast (B&W pages / all / off), with a *Reset to defaults* button. Detection thresholds stay internal constants of `ComicEpubBuilder`, covered by its `BuilderVersion`. Values are stored as JSON in `Settings.ComicRendering`; changing any value rebuilds all comic renditions on the next library sync.

### Edit view

Comics get a **Reading Direction** select (Left to right — default — / Right to left) stored in ComicInfo `Manga`, and a **Colour** select (Auto / Always colour / Always black & white) stored in the database. Contributor roles are limited to the ComicInfo roles. Cover upload/remove is hidden.

### Storage

**V5 migration** adds `BookFiles.ReadingDirection TEXT NULL`, `BookFiles.ColorMode TEXT NULL` and `Settings.ComicRendering TEXT NOT NULL DEFAULT '{}'`.

## Acceptance Criteria

- [x] `.cbr` files in the ebook folder are converted to `.cbz` on library sync and the original removed; corrupt archives are quarantined
- [x] CBZ comics are indexed with metadata from ComicInfo.xml, falling back to the file name
- [x] A fixed-layout EPUB is built at index time for every screen profile among registered devices
- [x] Registering a device with a new screen profile queues a library sync that builds its renditions
- [x] Sync advertises comics as `EPUB3FL` with the size of the device's own rendition; comics without a rendition are held back
- [x] Wide pages are rotated so the right side points up
- [x] Empty page margins are cropped; nearly screen-shaped pages fill the screen exactly
- [x] Colour is detected per page; the edit view can force colour or black & white
- [x] The edit view sets reading direction (left to right by default), written to ComicInfo.xml
- [x] Changing direction, colour mode or metadata rebuilds the renditions
- [x] Comic rendering tuning values are editable on the Settings page; changing them rebuilds all renditions
- [ ] Verified on a Kobo Libra Colour: comic reader opens, landscape spreads, reading direction, bookmarks

## Dependencies

- SharpCompress (RAR reading) and Magick.NET (image processing)
- Kobo device model codes captured by `KoboDeviceAuthHandler`
