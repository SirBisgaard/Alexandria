# Third-party notices

Alexandria is licensed under the [EUPL-1.2](LICENSE). It includes or uses the following third-party software and content, each under its own license.

## Bundled in this repository

| Component | Where | License | Source |
|---|---|---|---|
| kepubify v4.0.4 (Patrick Gaskin) | `Alexandria.DataAccess/Externals/kepubify`, `kepubify-darwin` | MIT | <https://github.com/pgaskin/kepubify> |
| jQuery v4.0.0 (OpenJS Foundation and contributors) | `Alexandria.Api/wwwroot/scripts/jquery.js` | MIT | <https://jquery.com/license> |
| Bootstrap v5.3.8 (The Bootstrap Authors) | `Alexandria.Api/wwwroot/scripts/bootstrap.bundle.min.js`, `styles/bootstrap.min.css` | MIT | <https://github.com/twbs/bootstrap> |
| GLightbox (Biati Digital) | `Alexandria.Api/wwwroot/scripts/glightbox.min.js`, `styles/glightbox.min.css` | MIT | <https://github.com/biati-digital/glightbox> |
| *Peter Pan* by J. M. Barrie, Standard Ebooks edition | `Alexandria.Test/TestData/J.M. Barrie - Peter Pan.epub` | CC0 1.0 (text in the US public domain) | <https://standardebooks.org> |

### kepubify license

```
MIT License

Copyright (c) Patrick Gaskin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Included in the Docker image

| Component | License | Source |
|---|---|---|
| FFmpeg and FFprobe, static builds from `mwader/static-ffmpeg` | GPL (the builds enable GPL components) | <https://github.com/wader/static-ffmpeg>, <https://ffmpeg.org/legal.html> |
| .NET runtime and ASP.NET Core (`mcr.microsoft.com/dotnet/aspnet`) | MIT | <https://github.com/dotnet/runtime>, <https://github.com/dotnet/aspnetcore> |

FFmpeg is run as a separate program; Alexandria doesn't link against it.

## NuGet packages

| Package | License |
|---|---|
| Magick.NET-Q8-AnyCPU (includes ImageMagick) | Apache-2.0 (ImageMagick: ImageMagick License) |
| SharpCompress | MIT |
| Microsoft.Data.Sqlite, Microsoft.Extensions.*, Microsoft.AspNetCore.*, System.IO.Hashing | MIT |
| MSTest, Microsoft.AspNetCore.Mvc.Testing (tests only) | MIT |

## Trademarks

Kobo is a trademark of Rakuten Kobo Inc. Audiobookshelf is a trademark of its respective owner. Alexandria is not affiliated with or endorsed by either.
