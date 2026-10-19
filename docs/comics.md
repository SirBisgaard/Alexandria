# Comics

Alexandria sends CBZ and CBR comics to your Kobo as **fixed-layout EPUBs**, the format that opens the Kobo's own comic reader. It builds a separate version for every screen size you own, so each page fills the screen properly.

- [Adding comics](#adding-comics)
- [Metadata: ComicInfo.xml](#metadata-comicinfoxml)
- [One rendition per screen](#one-rendition-per-screen)
- [Rendering settings](#rendering-settings)
- [Per-comic settings](#per-comic-settings)
- [Troubleshooting](#troubleshooting)

## Adding comics

Put `.cbz` and `.cbr` files **directly in the ebook folder**, next to your EPUBs.

- **CBR files are converted to CBZ** before every index: the pages are repacked into a `.cbz` with the same name, and the `.cbr` is deleted. If a `.cbz` with that name already exists, the CBR is left alone. A CBR that can't be read is moved to `{EbookFolder}/alexandria-conversion-failed/`.
- Pages are the images in the archive (jpg, png, gif, webp, bmp, avif, jxl), sorted naturally (`2` before `10`).
- The cover is the page marked `FrontCover` in ComicInfo.xml, otherwise the first page. Comic covers can't be replaced in the web UI.

## Metadata: ComicInfo.xml

Metadata lives in the [ComicInfo.xml](https://anansi-project.github.io/docs/comicinfo/intro) inside the CBZ, the format used by most comic tools.

| Field | ComicInfo element |
|---|---|
| Title | `Title` |
| Series and number | `Series`, `Number` (or `Volume`) |
| Authors | `Writer` |
| Contributors | `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`, `Translator` |
| Description | `Summary` |
| Publisher, language, date | `Publisher`, `LanguageISO`, `Year`/`Month`/`Day` |
| Genres | `Genre` |
| ISBN | `GTIN` |
| Reading direction | `Manga` (`YesAndRightToLeft` = right to left) |

Without a ComicInfo.xml, the filename is parsed as `Authors - (Series #N - )Title`.

Editing a comic in the web UI writes the changes back into its ComicInfo.xml. The colour mode can't be stored in ComicInfo, so it lives in the database.

## One rendition per screen

Kobos have very different screens, so one file can't fit them all. For every **screen profile** among your paired devices, Alexandria builds a fixed-layout EPUB of each comic. Each page is:

1. **cropped**: empty margins trimmed so the art is larger (the first page is left as it is if it's in colour),
2. **rotated or split**: double-page spreads are turned a quarter turn (right edge up) to fill the screen sideways, or split into two pages. Very wide panoramas (1.8:1 or wider) are always rotated, even when splitting,
3. **coloured or greyscale**: colour is kept only on colour screens, and in *Auto* mode only on pages that actually contain colour,
4. **fitted**: shrunk to fit the screen, never enlarged. Pages whose shape is very close to the screen's fill it exactly instead of leaving thin bars,
5. **levelled**: contrast stretched for e-ink, unless the page is deliberately low-contrast.

Each page becomes its own fixed-layout page. Portrait shows one page, and landscape shows a two-page spread.

| Profile | Devices |
|---|---|
| 600×800 grey | Kobo Mini, Touch 2.0 |
| 758×1024 grey | Kobo Nia |
| 1072×1448 grey | Glo HD, Clara HD, Clara 2E, Clara BW |
| 1072×1448 colour | Clara Colour |
| 1080×1430 grey | Aura H2O Edition 2 |
| 1264×1680 grey | Libra 2 |
| 1264×1680 colour | Libra Colour |
| 1404×1872 grey | Aura ONE, Elipsa, Elipsa 2E |
| 1440×1920 grey | Forma, Sage |

- Renditions are stored at `{DataFolder}/comics/{profile}/{BookUuid}.{signature}.epub`. The signature is a hash of the source file's size and modified time, its reading direction and colour mode, the rendering settings, and the renderer's version. Change any of them and a new rendition is built on the next sync.
- Folders for screen profiles no paired device uses any more are cleaned up.
- When a Kobo with a new screen size connects for the first time, a library sync starts on its own to build renditions for it, unless another library task is already running. In that case, run a library sync yourself.
- **Kobos that aren't in the table above get no comics at all.** That includes older models like the Glo, Aura, Aura HD, Aura H2O and Libra H2O. Ebooks and audiobooks still work on them.
- During sync, a comic that has no rendition for the requesting device yet is **held back** and offered on a later sync, once the rendition exists. The Kobo is told the rendition's file size, not the CBZ's.

## Rendering settings

**More → Settings → Comics** applies to all comics. Changing anything rebuilds all renditions on the next library sync.

| Setting | Options | Default | What it does |
|---|---|---|---|
| Double-page spreads | Rotate · Split · Split and rotate | Rotate | Rotate turns wide pages a quarter turn to fill the screen. Split cuts them into two pages in reading order. Split and rotate shows both |
| Margin cropping | Crop margins · Off | Crop margins | Trims empty borders so the art is bigger |
| Cropping strength | Gentle · Normal · Aggressive | Normal | Gentle only crops plain borders. Aggressive also crops textured or faintly marked borders, with a higher risk of trimming art |
| Preserve margin % | 0–10 | 1 | How much border to keep around the art after cropping |
| Image quality | 1–100 | 85 | JPEG quality of the pages. Higher is sharper but makes larger files |
| Auto-contrast | Black & white pages · All pages · Off | Black & white pages | Stretches pages to full black and white for e-ink |

**Reset to defaults** restores all of these.

## Per-comic settings

When you edit a comic:

- **Reading direction:** *Left to right* (default) or *Right to left (manga)*. Stored in ComicInfo's `Manga` element.
- **Colour:** *Auto (per page)*, *Always colour*, or *Always black & white*. On colour screens, Auto keeps colour on pages that contain colour and makes the rest greyscale. Black-and-white screens always get greyscale.

## Troubleshooting

| Problem | Fix |
|---|---|
| A comic doesn't show up on one Kobo | Its rendition for that screen isn't built yet: run a library sync in the web UI, then sync the Kobo. If the model isn't in the [screen table](#one-rendition-per-screen), it can't get comics |
| A CBR disappeared | It was converted to a CBZ with the same name. If it's in `alexandria-conversion-failed/`, it couldn't be read |
| Art is cut off at the edges | Lower the **Cropping strength**, raise **Preserve margin %**, or turn cropping off |
| Manga pages go the wrong way | Set the comic's reading direction to right to left |
