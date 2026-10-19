# Alexandria documentation

The [main README](../README.md) covers what Alexandria is and how to get it running. These pages cover everything else.

## Guides

| Page | For |
|---|---|
| [How it works](how-it-works.md) | The proxy, the Kobo sync protocol, book identity, reading progress, indexing, editing, what's stored where |
| [Running on a server](running-on-a-server.md) | Docker, permissions, configuration, reverse proxies, NAS setups, upgrades, backups, security, troubleshooting |
| [Building and contributing](building-and-contributing.md) | Building from source, project layout, tests, conventions, reverse-engineering the Kobo, releases |

## Topics

| Page | For |
|---|---|
| [Audiobooks](audiobooks.md) | M4B and MP3 folders, the three M4B modes, tags, listening sessions |
| [Comics](comics.md) | CBZ/CBR, ComicInfo.xml, per-screen renditions, rendering settings |
| [Audiobookshelf sync](audiobookshelf.md) | Two-way progress sync with Audiobookshelf |

## Frequently asked questions

**Does this replace my Kobo account?**
No. Alexandria passes everything it doesn't handle through to the Kobo store, so your account, purchases, the store and firmware updates keep working. Your own books are added alongside. See [The proxy](how-it-works.md#the-proxy-intercept-or-pass-through).

**Do I have to modify my Kobo?**
Only one line in `.kobo/Kobo/Kobo eReader.conf` (`api_endpoint`). No custom firmware, no jailbreak. Put the original line back to undo it.

**Will my files be changed?**
Yes. Metadata and cover edits are written into the files, and file management renames them. A library sync never edits metadata, but it does repack CBR comics as CBZ (deleting the CBR), may add `cover.jpg` to MP3 audiobook folders, and runs the M4B treatment you chose (`ConvertAtIndex` deletes each M4B). Browsing the web UI and syncing a Kobo don't change files. See [Editing metadata](how-it-works.md#editing-metadata).

**Can I organise my library in subfolders?**
Not yet. Ebooks and comics must sit directly in the ebook folder, and audiobooks directly in the audiobook folder (M4Bs as files, MP3 audiobooks as one subfolder each).

**Does reading progress sync between my Kobos?**
Yes. Progress is stored once per book and sent to every paired Kobo on its next sync. See [Reading progress](how-it-works.md#reading-progress).

**Does every person get their own progress?**
No. One Alexandria instance is one library with one set of progress. Users are for managing the library and for per-user integrations.

**Do I need HTTPS?**
Not at home. Plain HTTP to a LAN address works. To sync away from home, use HTTPS behind a reverse proxy. See [Networking](running-on-a-server.md#networking-publicurl-https-and-reverse-proxies).

**Which Kobos are supported?**
Any Kobo that syncs with the Kobo store. Audiobooks need a Kobo with Bluetooth audio. Comics only go to the models in [the screen list](comics.md#one-rendition-per-screen). Older models like the Glo, Aura and Libra H2O get no comics.

**Do I need a Kobo account?**
Yes. The Kobo signs in to Kobo as usual, through Alexandria, and has to be on Wi-Fi that can reach your Alexandria server.

**I removed a book on my Kobo. Where did it go?**
*Remove* on a Kobo archives the book in Alexandria, which removes it from **every** paired Kobo on their next sync. It isn't deleted. Archived books stay in My Books with an *Archived* label. Choose **Unarchive** in the book's menu to bring it back, and it's sent to your devices again.

**I deleted a file, but the book is still on my Kobo.**
Deleting a file removes the book from Alexandria, but Alexandria can't tell the Kobo about a book it no longer knows. Remove it on the Kobo. Next time, archive the book and sync before deleting the file.

**Can I read in the browser?**
No, and that's on purpose. Reading and listening belong on the Kobo. Alexandria is the librarian, not the reading room.

**I reinstalled and now every book is on my Kobo twice.**
The database was lost, and with it the IDs the Kobo knows your books by. Restore it from a backup, or remove the old copies on the Kobo. See [Backups](running-on-a-server.md#backups).

**Something is broken. Where do I look?**
`{DataFolder}/exceptions.log` and `docker compose logs`. Then see [Troubleshooting](running-on-a-server.md#troubleshooting), or open an issue.
