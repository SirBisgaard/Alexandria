# FAQ and How Alexandria Works

## Story

As a logged-in user of Alexandria,
Wants to see how Alexandria reads my files and what each metadata tag becomes in the library.
So that I can understand why a book looks the way it does, 
and know how to edit the source file to change fields Alexandria parses it correctly.

## Technical

Add a new "How it works" entry to the More page, sitting alongside the existing "About Alexandria" item. 
Opening it launches a modal titled "How Alexandria reads your files".

The modal has a tab strip at the top with one tab per supported file type:
- **EPUB** — covers ebooks indexed by `FileEbookIndexer` / `EpubMetadataHandler`
- **M4B** — covers audiobooks indexed by `FileAudiobookIndexer` / `M4bMetadataHandler`

Each tab shows a two-column layout:
- **Left column** — a `<pre>` block with a representative sample of the raw tags read from the file. 
  For EPUB, this is the relevant slice of `content.opf` (`<dc:title>`, `<dc:creator>`, `<dc:identifier opf:scheme="ISBN">`, 
  `<dc:language>`, `<dc:publisher>`, `<dc:date>`, `<dc:description>`, `<dc:subject>`, `<dc:rights>`, `<dc:source>`, 
  `<dc:type>`, `<dc:relation>`, `<dc:coverage>`, `<dc:format>`, `<meta property="belongs-to-collection">`, 
  `<meta name="calibre:series">`, and the EPUB 3 `<meta refines>` / EPUB 2 `opf:role` contributor examples). 
  For M4B, this is the relevant slice of `ffprobe -show_format -show_chapters` JSON output (`format.tags.title`, 
  `format.tags.artist`, `format.tags.album_artist`, `format.tags.album`, `format.tags.composer`, `format.tags.date`, 
  `format.tags.description`, `format.tags.genre`, `format.tags.publisher`, `format.tags.language`, `format.duration`, 
  and the `chapters[]` array).
- **Right column** — the corresponding fields on the Book DTO (`Title`, `Subtitle`, `Authors`, `Isbn`, `Language`, 
  `Publisher`, `PublicationDate`, `Description`, `Subjects`, `Rights`, `Source`, `Type`, `Relation`, `Coverage`, 
  `Format`, `Series.Name`, `Series.Index`, `Contributors`, `Duration`, `Chapters`). 
  Each tag on the left is visually connected to the field it becomes on the right (e.g. matching numbered markers, 
  hover highlight, or a simple two-column table). The mapping must reflect the actual extraction logic, 
  including fallbacks (e.g. ISBN tried as `ISBN:`, `urn:isbn:`, then plain, and EPUB 2 `opf:role` used when EPUB 3 
  `meta refines` is not present).

Also include a short preamble paragraph at the top of the modal explaining:
- Alexandria indexes the `EbookFolder` and `AudiobookFolder` at boot (or on manual sync) and derives a deterministic 
  UUID v5 for each book from `title:authors`, so renaming a file does not create a duplicate but retitling in the 
  metadata will re-key the book.
- Edits made from Alexandria's edit form are embedded back into the source file, then re-indexed, preserving the UUID.
- Fields that are not present in the source file are left blank; Alexandria never fabricates metadata.

The content is static and lives entirely in the frontend — no new API endpoints are required. 
The `<pre>` samples are shipped as part of the modal's HTML (or a sibling JS/JSON fixture) so they render instantly.

## Acceptance Criteria

- [ ] More page has a "How it works" item below "About Alexandria"
- [ ] Clicking it opens a modal with a tab strip containing at least "EPUB" and "M4B" tabs
- [ ] Each tab shows a `<pre>` block with a representative sample of the raw tags read from that file type
- [ ] Each tag in the sample is paired with the Book DTO field it populates, and the pairing is visually obvious
- [ ] EPUB tab documents the full set of `dc:*` tags, `meta refines` / `opf:role` contributor handling, and both series metadata variants (`belongs-to-collection`, `calibre:series`)
- [ ] M4B tab documents the `ffprobe` tag names (`title`, `artist`, `album_artist`, `album`, `composer`, `date`, `description`, `genre`, `publisher`, `language`, `duration`) and the `chapters[]` array
- [ ] The modal includes a preamble explaining deterministic UUID generation, edit-back-to-file behavior, and the no-fabrication rule
- [ ] The modal works on mobile widths (tabs stack or scroll horizontally, `<pre>` blocks scroll rather than overflow)
- [ ] No new backend endpoints are added

## Dependencies

The user is authenticated and logged into Alexandria.
