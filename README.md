# Alexandria

Your own books and audiobooks on your Kobo, just like the ones you bought from the store.

Alexandria is a small self-hosted library for Kobo e-readers. Put your EPUBs, comics and audiobooks in a folder, and they show up on your Kobo with covers, series and reading progress. There's no USB and no sideloading. Your Kobo syncs the way it always does.

![Alexandria tour](Screenshots/tour.gif)

## Features

### On your Kobo

- **Ebooks**: EPUBs are converted to KEPUB automatically, so they open in the Kobo's own reader.
- **Audiobooks**: M4B files and folders of MP3 chapters play in the Kobo's audiobook player.
- **Comics**: CBZ and CBR files are turned into fixed-layout books that fit your device's screen.
- **Covers, series and authors**: books look the same as store purchases.
- **Reading progress**: progress and finished status sync both ways, so you can start on one Kobo and pick up on another.
- **The Kobo store still works**: anything Alexandria doesn't handle is passed through to Kobo unchanged.

### In the browser

- Browse and search books, authors, series and collections.
- Edit titles, authors, series and covers. Changes are written back into your files.
- Sync your library on demand and watch it happen live.
- Manage devices, users and settings.
- Light and dark mode.

## Getting started

### 1. Run it

```yaml
services:
  alexandria:
    image: ghcr.io/sirbisgaard/alexandria:latest
    ports:
      - 8080:8080
    restart: always
    volumes:
      - /path/to/your/ebooks:/books/ebooks
      - /path/to/your/audiobooks:/books/audiobooks
      - ./data:/data
    environment:
      - AlexandriaSettings__PublicUrl=https://alexandria.example.com
      - AlexandriaSettings__DataFolder=/data
      - AlexandriaSettings__EbookFolder=/books/ebooks
      - AlexandriaSettings__AudiobookFolder=/books/audiobooks
```

```sh
docker compose up -d
```

`PublicUrl` is the address your Kobo uses to reach Alexandria, so it can't be `localhost`.

### 2. Sign in

Open the web UI and sign in with username `alexander` and password `TheGreat`. **Change the password right away.**

### 3. Connect your Kobo

Pair a new device on the Devices page to get its device key. Then connect the Kobo over USB, open `.kobo/Kobo/Kobo eReader.conf` and change only the `api_endpoint` line:

```conf
[OneStoreServices]
api_endpoint=https://alexandria.example.com/api/kobo/<device-key>
```

Eject the Kobo and sync. Your library appears.

## Audiobooks

M4B audiobooks can be handled in three ways (Settings in the web UI):

- **Convert at index** (recommended): the M4B is turned into MP3 chapters once and the original is removed. This gives the best compatibility on the Kobo.
- **Transcode at index**: MP3 chapters are made once and the M4B is kept. This uses more disk space.
- **Nothing**: chapters are converted when the Kobo asks for them. This saves disk space, but long chapters can time out.

## Building from source

Requires the .NET 10 SDK and `ffmpeg` on the path.

```sh
dotnet build Alexandria.slnx
dotnet test Alexandria.Test/Alexandria.Test.csproj
dotnet run --project Alexandria.Api/Alexandria.Api.csproj
```

## Thanks

- **[FFmpeg](https://ffmpeg.org/)** for everything audio.
- **[kepubify](https://pgaskin.net/kepubify/)** for EPUB to KEPUB conversion (bundled).
- **Kobo**, for making devices that are a pleasure to read on.

## Contributing

Issues, ideas and pull requests are welcome. Alexandria stays deliberately small: it manages your library, and the Kobo does the reading.

## Disclaimer

Alexandria is not associated with Rakuten Kobo Inc. in any way, and is not endorsed or supported by them.
Kobo is a trademark of Rakuten Kobo Inc.

### Use of LLMs (AI)

LLMs were used to write parts of this code. I review it, I test it, I'm responsible for it.

## License

[EUPL-1.2](./LICENSE)
