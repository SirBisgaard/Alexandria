using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Interfaces;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files.Books.Comics;

/// <summary>
/// Builds the fixed-layout EPUB a Kobo receives for a comic, following Kobo's FXL spec and the
/// page pipeline of Kindle Comic Converter (KCC). The user-facing choices come from
/// <see cref="ComicRenderingSettings"/>; detection thresholds are the constants below.
/// <list type="bullet">
/// <item>empty margins around the art are cropped away (<see cref="PagePipeline.CropMargins"/>),
/// except on a coloured cover;</item>
/// <item>landscape pages wider than <see cref="SpreadRatio"/> (double-page spreads) are rotated a
/// quarter turn counter-clockwise (right edge up), split into their two pages, or both — per
/// <see cref="ComicSpreadHandling"/>;</item>
/// <item>pages are kept in colour only on colour screens, and in <see cref="ComicColorMode.Auto"/>
/// only when they actually contain colour;</item>
/// <item>pages larger than the screen are shrunk to fit it (never enlarged); a page whose shape is
/// within <see cref="FillRatioTolerance"/> of the screen's fills it exactly instead, losing a sliver
/// at the edges rather than leaving thin bars;</item>
/// <item>pages are auto-levelled per <see cref="ComicAutoContrast"/> unless deliberately
/// low-contrast, then saved as JPEG.</item>
/// </list>
/// Each page is its own XHTML document whose viewport equals the image size, and the OPF declares
/// <c>rendition:layout</c> pre-paginated with <c>rendition:spread</c> landscape — portrait shows one
/// page, landscape a two-page spread. The device only treats the book as fixed layout because sync
/// advertises it with the <c>EPUB3FL</c> format; synced books skip Nickel's own layout detection.
/// </summary>
public class ComicEpubBuilder(ILogger<ComicEpubBuilder> logger) : IComicEpubBuilder
{
    /// <summary>
    /// Bump whenever the rendering logic or one of the constants below changes output, so existing
    /// renditions are rebuilt. User choices in <see cref="ComicRenderingSettings"/> invalidate
    /// renditions by themselves.
    /// </summary>
    public const int BuilderVersion = 2;

    /// <summary>Width/height above which a landscape page is treated as a double-page spread (KCC's threshold).</summary>
    private const double SpreadRatio = 1.16;

    /// <summary>Width/height from which <see cref="ComicSpreadHandling.Split"/> rotates instead of splitting — a panorama whose halves would each still be landscape (KCC's BISECT_THRESHOLD).</summary>
    private const double PanoramaRatio = 1.8;

    /// <summary>
    /// Maximum difference between page and screen height/width ratios for which a page larger than
    /// the screen is scaled to fill it and centre-cropped rather than fitted inside it (KCC's
    /// AUTO_CROP_THRESHOLD). At Kobo aspect ratios this trims at most about 0.6 % per side, which
    /// the default preserved margin covers.
    /// </summary>
    private const double FillRatioTolerance = 0.015;

    /// <summary>A pixel counts as coloured when its RGB channels differ by more than this (0–255). Yellowed paper and JPEG noise stay below it.</summary>
    private const int ColorChromaThreshold = 40;

    /// <summary>
    /// Share of coloured pixels from which a page is a colour page in <see cref="ComicColorMode.Auto"/>.
    /// Measured on real books: plain text pages sit at 0.02–0.04 %, pages whose only colour is a red
    /// heading or ornament at 0.15–1 %, illustrations far higher. Keeping colour for a heading is
    /// free on Kaleido screens, which only drop resolution where pixels are actually coloured.
    /// </summary>
    private const double ColorPixelShare = 0.001;

    /// <summary>Pages whose darkest-to-lightest range (0–255) is below this are not auto-levelled — such low contrast is usually intentional (fog, flashbacks). KCC's 255−96 cut-off.</summary>
    private const int AutoLevelMinimumRange = 159;

    /// <summary>Longest side, in pixels, of the copy margins are detected on — large enough that one-pixel frame lines and small captions survive the averaging.</summary>
    private const int MarginAnalysisSize = 2000;

    /// <summary>Margin cropping never removes more than this share of the page from any side.</summary>
    private const double MaxMarginCropPerSide = 0.1;

    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Ops = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace Ncx = "http://www.daisy.org/z3986/2005/ncx/";

    public void Build(Book book, ComicDeviceProfile profile, ComicRenderingSettings rendering, string outputPath, Action<int, int>? reportProgress = null, CancellationToken cancellationToken = default)
    {
        var direction = book.File.ReadingDirection ?? ComicReadingDirection.LeftToRight;
        var pipeline = new PagePipeline(rendering, direction);
        var colorMode = book.File.ColorMode ?? ComicColorMode.Auto;

        using var stream = new FileStream(book.File.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = CbzFileHandler.GetPageEntries(zip);
        if (entries.Count == 0)
            throw new InvalidDataException($"No page images found in '{book.File.SourcePath}'.");

        var rendered = new List<RenderedPage>[entries.Count];
        var zipGate = new Lock();
        var completed = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree, CancellationToken = cancellationToken };
        Parallel.For(0, entries.Count, options, i =>
        {
            byte[] raw;
            lock (zipGate)
                raw = ReadAllBytes(entries[i]);
            rendered[i] = pipeline.RenderPage(raw, i == 0, profile, colorMode);
            reportProgress?.Invoke(Interlocked.Increment(ref completed), entries.Count);
        });

        var chapters = BuildChapters(book, entries, rendered);
        var pages = rendered.SelectMany(p => p).ToList();
        WriteEpub(book, profile, direction, pages, chapters, outputPath);

        var colorPages = pages.Count(p => p.IsColor);
        var spreads = rendered.Count(p => p.Count > 1 || p[0].Kind == PageKind.Rotated);
        logger.LogInformation("Built {Profile} rendition of '{Title}': {Pages} pages from {SourcePages} source pages ({ColorPages} colour, {Spreads} spreads)", profile.Key, book.Title, pages.Count, entries.Count, colorPages, spreads);
    }

    /// <summary>
    /// One navigation entry per source folder when the archive groups pages into folders
    /// (typically one per issue in a collected edition); otherwise a single entry for the book.
    /// </summary>
    private static List<(string Title, int FirstPage)> BuildChapters(Book book, List<ZipArchiveEntry> entries, List<RenderedPage>[] rendered)
    {
        var chapters = new List<(string Title, int FirstPage)>();
        string? currentFolder = null;
        var outputIndex = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var folder = Path.GetDirectoryName(CbzFileHandler.NormalizePath(entries[i].FullName))?.Replace('\\', '/') ?? string.Empty;
            if (folder != currentFolder)
            {
                var name = folder.Length == 0 ? book.Title : folder[(folder.LastIndexOf('/') + 1)..];
                chapters.Add((name, outputIndex));
                currentFolder = folder;
            }
            outputIndex += rendered[i].Count;
        }
        return chapters.Count > 1 ? chapters : [(book.Title, 0)];
    }

    private static void WriteEpub(Book book, ComicDeviceProfile profile, ComicReadingDirection direction, List<RenderedPage> pages, List<(string Title, int FirstPage)> chapters, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = outputPath + ".tmp";
        try
        {
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var epub = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteText(epub, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
                WriteXml(epub, "META-INF/container.xml", BuildContainer());
                WriteText(epub, "OEBPS/style.css", "@page { margin: 0; }\nbody { display: block; margin: 0; padding: 0; }\n.page { text-align: center; }\nimg { display: block; margin: 0; padding: 0; }\n", CompressionLevel.Optimal);

                for (var i = 0; i < pages.Count; i++)
                {
                    var entry = epub.CreateEntry($"OEBPS/Images/{PageName(i)}.jpg", CompressionLevel.NoCompression);
                    using (var output = entry.Open())
                        output.Write(pages[i].Jpeg);
                    WriteText(epub, $"OEBPS/Text/{PageName(i)}.xhtml", BuildPageXhtml(i, pages[i]), CompressionLevel.Optimal);
                }

                WriteXml(epub, "OEBPS/nav.xhtml", BuildNav(book, chapters));
                WriteXml(epub, "OEBPS/toc.ncx", BuildNcx(book, chapters));
                WriteXml(epub, "OEBPS/content.opf", BuildOpf(book, profile, direction, pages));
            }
            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string PageName(int index) => $"p{index + 1:D4}";

    private static string BuildPageXhtml(int index, RenderedPage page)
    {
        var width = page.Width.ToString(CultureInfo.InvariantCulture);
        var height = page.Height.ToString(CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head>
            <title>Page {index + 1}</title>
            <link href="../style.css" type="text/css" rel="stylesheet"/>
            <meta name="viewport" content="width={width}, height={height}"/>
            </head>
            <body>
            <div class="page"><img src="../Images/{PageName(index)}.jpg" width="{width}" height="{height}" alt=""/></div>
            </body>
            </html>
            """;
    }

    private static XDocument BuildContainer()
    {
        XNamespace container = "urn:oasis:names:tc:opendocument:xmlns:container";
        return new XDocument(new XElement(container + "container", new XAttribute("version", "1.0"),
            new XElement(container + "rootfiles",
                new XElement(container + "rootfile", new XAttribute("full-path", "OEBPS/content.opf"), new XAttribute("media-type", "application/oebps-package+xml")))));
    }

    private static XDocument BuildOpf(Book book, ComicDeviceProfile profile, ComicReadingDirection direction, List<RenderedPage> pages)
    {
        var metadata = new XElement(Opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XElement(Dc + "identifier", new XAttribute("id", "BookId"), $"urn:uuid:{book.BookUuid}"),
            new XElement(Dc + "title", book.Title),
            new XElement(Dc + "language", string.IsNullOrWhiteSpace(book.Language) ? "en" : book.Language));
        foreach (var author in book.Authors)
            metadata.Add(new XElement(Dc + "creator", author.Name));
        foreach (var contributor in book.Contributors)
            metadata.Add(new XElement(Dc + "contributor", contributor.Name));
        if (!string.IsNullOrWhiteSpace(book.Publisher))
            metadata.Add(new XElement(Dc + "publisher", book.Publisher));
        if (!string.IsNullOrWhiteSpace(book.PublicationDate))
            metadata.Add(new XElement(Dc + "date", book.PublicationDate));
        if (!string.IsNullOrWhiteSpace(book.Description))
            metadata.Add(new XElement(Dc + "description", book.Description));
        metadata.Add(new XElement(Opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        if (book.Series is not null)
        {
            metadata.Add(new XElement(Opf + "meta", new XAttribute("property", "belongs-to-collection"), new XAttribute("id", "series"), book.Series.Name));
            metadata.Add(new XElement(Opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "collection-type"), "series"));
            if (book.Series.Index is not null)
                metadata.Add(new XElement(Opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "group-position"), book.Series.Index.Value.ToString("G29", CultureInfo.InvariantCulture)));
        }
        metadata.Add(
            new XElement(Opf + "meta", new XAttribute("name", "cover"), new XAttribute("content", "cover")),
            new XElement(Opf + "meta", new XAttribute("property", "rendition:layout"), "pre-paginated"),
            new XElement(Opf + "meta", new XAttribute("property", "rendition:spread"), "landscape"),
            new XElement(Opf + "meta", new XAttribute("property", "rendition:orientation"), "auto"),
            new XElement(Opf + "meta", new XAttribute("name", "original-resolution"), new XAttribute("content", $"{profile.Width}x{profile.Height}")),
            new XElement(Opf + "meta", new XAttribute("name", "book-type"), new XAttribute("content", "comic")));

        var manifest = new XElement(Opf + "manifest",
            new XElement(Opf + "item", new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"), new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")),
            new XElement(Opf + "item", new XAttribute("id", "ncx"), new XAttribute("href", "toc.ncx"), new XAttribute("media-type", "application/x-dtbncx+xml")),
            new XElement(Opf + "item", new XAttribute("id", "css"), new XAttribute("href", "style.css"), new XAttribute("media-type", "text/css")));
        for (var i = 0; i < pages.Count; i++)
        {
            manifest.Add(new XElement(Opf + "item", new XAttribute("id", $"page{i + 1}"), new XAttribute("href", $"Text/{PageName(i)}.xhtml"), new XAttribute("media-type", "application/xhtml+xml")));
            var image = new XElement(Opf + "item", new XAttribute("id", i == 0 ? "cover" : $"img{i + 1}"), new XAttribute("href", $"Images/{PageName(i)}.jpg"), new XAttribute("media-type", "image/jpeg"));
            if (i == 0)
                image.Add(new XAttribute("properties", "cover-image"));
            manifest.Add(image);
        }

        var isRightToLeft = direction == ComicReadingDirection.RightToLeft;
        var spine = new XElement(Opf + "spine", new XAttribute("toc", "ncx"), new XAttribute("page-progression-direction", isRightToLeft ? "rtl" : "ltr"));
        var sides = AssignPageSpreads(pages, isRightToLeft);
        for (var i = 0; i < pages.Count; i++)
            spine.Add(new XElement(Opf + "itemref", new XAttribute("idref", $"page{i + 1}"), new XAttribute("properties", $"rendition:page-spread-{sides[i]}")));

        return new XDocument(new XElement(Opf + "package",
            new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "BookId"),
            metadata, manifest, spine));
    }

    /// <summary>
    /// KCC's spread placement: pages alternate sides starting on the reading-direction's leading
    /// side. The two halves of a split spread are pinned to the leading and trailing sides so they
    /// reunite as one spread when the device is held in landscape; a rotated spread is centred on
    /// its own. The alternation restarts on the leading side after either.
    /// </summary>
    private static string[] AssignPageSpreads(List<RenderedPage> pages, bool isRightToLeft)
    {
        var leading = isRightToLeft ? "right" : "left";
        var trailing = isRightToLeft ? "left" : "right";
        var sides = new string[pages.Count];
        var next = leading;
        for (var i = 0; i < pages.Count; i++)
        {
            switch (pages[i].Kind)
            {
                case PageKind.SpreadFirst:
                    sides[i] = leading;
                    next = leading;
                    break;
                case PageKind.SpreadSecond:
                    sides[i] = trailing;
                    next = leading;
                    break;
                case PageKind.Rotated:
                    sides[i] = "center";
                    next = leading;
                    break;
                default:
                    sides[i] = next;
                    next = next == leading ? trailing : leading;
                    break;
            }
        }
        return sides;
    }

    private static XDocument BuildNav(Book book, List<(string Title, int FirstPage)> chapters)
    {
        var list = new XElement(Xhtml + "ol", chapters.Select(c =>
            new XElement(Xhtml + "li", new XElement(Xhtml + "a", new XAttribute("href", $"Text/{PageName(c.FirstPage)}.xhtml"), c.Title))));
        return new XDocument(
            new XDocumentType("html", null, null, null),
            new XElement(Xhtml + "html", new XAttribute(XNamespace.Xmlns + "epub", Ops),
                new XElement(Xhtml + "head", new XElement(Xhtml + "title", book.Title)),
                new XElement(Xhtml + "body",
                    new XElement(Xhtml + "nav", new XAttribute(Ops + "type", "toc"), new XAttribute("id", "toc"), list))));
    }

    private static XDocument BuildNcx(Book book, List<(string Title, int FirstPage)> chapters)
    {
        var navMap = new XElement(Ncx + "navMap", chapters.Select((c, i) =>
            new XElement(Ncx + "navPoint", new XAttribute("id", $"nav{i + 1}"), new XAttribute("playOrder", i + 1),
                new XElement(Ncx + "navLabel", new XElement(Ncx + "text", c.Title)),
                new XElement(Ncx + "content", new XAttribute("src", $"Text/{PageName(c.FirstPage)}.xhtml")))));
        return new XDocument(new XElement(Ncx + "ncx", new XAttribute("version", "2005-1"),
            new XElement(Ncx + "head", new XElement(Ncx + "meta", new XAttribute("name", "dtb:uid"), new XAttribute("content", $"urn:uuid:{book.BookUuid}"))),
            new XElement(Ncx + "docTitle", new XElement(Ncx + "text", book.Title)),
            navMap));
    }

    private static void WriteText(ZipArchive epub, string name, string content, CompressionLevel compression)
    {
        var entry = epub.CreateEntry(name, compression);
        using var output = entry.Open();
        output.Write(new UTF8Encoding(false).GetBytes(content));
    }

    private static void WriteXml(ZipArchive epub, string name, XDocument document)
    {
        var entry = epub.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        document.Save(writer);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Per-page image processing for one build, with that build's settings.</summary>
    private sealed class PagePipeline(ComicRenderingSettings rendering, ComicReadingDirection direction)
    {
        private readonly (int ContentDifference, double FrameUniformity) cropping = rendering.CroppingStrength switch
        {
            ComicCroppingStrength.Gentle => (16, 0.92),
            ComicCroppingStrength.Aggressive => (40, 0.75),
            _ => (24, 0.85)
        };

        public List<RenderedPage> RenderPage(byte[] raw, bool isFirstPage, ComicDeviceProfile profile, ComicColorMode colorMode)
        {
            using var image = new MagickImage(raw);
            image.AutoOrient();
            if (image.ColorSpace == ColorSpace.CMYK)
                image.ColorSpace = ColorSpace.sRGB;

            var isColorful = IsColorful(image);
            var keepColor = profile.IsColor && colorMode switch
            {
                ComicColorMode.Color => true,
                ComicColorMode.Grayscale => false,
                _ => isColorful
            };

            using var page = image.Clone();
            if (rendering.MarginCropping == ComicMarginCropping.Margins && !(isFirstPage && isColorful))
                CropMargins(page);

            var ratio = (double)page.Width / page.Height;
            if (ratio <= SpreadRatio)
                return [Finish(page.Clone(), PageKind.Single, keepColor, profile)];

            var split = rendering.SpreadHandling != ComicSpreadHandling.Rotate && ratio < PanoramaRatio;
            var rotate = rendering.SpreadHandling != ComicSpreadHandling.Split || !split;
            var pages = new List<RenderedPage>();
            if (split)
            {
                var halfWidth = page.Width / 2;
                var leftHalf = Crop(page, 0, halfWidth);
                var rightHalf = Crop(page, halfWidth, page.Width - halfWidth);
                var (first, second) = direction == ComicReadingDirection.RightToLeft ? (rightHalf, leftHalf) : (leftHalf, rightHalf);
                pages.Add(Finish(first, PageKind.SpreadFirst, keepColor, profile));
                pages.Add(Finish(second, PageKind.SpreadSecond, keepColor, profile));
            }
            if (rotate)
            {
                var rotated = page.Clone();
                rotated.Rotate(-90);
                pages.Add(Finish(rotated, PageKind.Rotated, keepColor, profile));
            }
            return pages;
        }

        private static IMagickImage<byte> Crop(IMagickImage<byte> image, uint x, uint width)
        {
            var part = image.Clone();
            part.Crop(new MagickGeometry((int)x, 0, width, image.Height));
            part.ResetPage();
            return part;
        }

        private RenderedPage Finish(IMagickImage<byte> part, PageKind kind, bool keepColor, ComicDeviceProfile profile)
        {
            using (part)
            {
                part.FilterType = FilterType.Lanczos;
                var largerThanScreen = part.Width > profile.Width || part.Height > profile.Height;
                var screenShaped = Math.Abs((double)part.Height / part.Width - (double)profile.Height / profile.Width) < FillRatioTolerance;
                if (largerThanScreen && screenShaped)
                {
                    part.Resize(new MagickGeometry((uint)profile.Width, (uint)profile.Height) { FillArea = true });
                    part.Crop((uint)profile.Width, (uint)profile.Height, Gravity.Center);
                    part.ResetPage();
                }
                else if (largerThanScreen)
                {
                    part.Resize(new MagickGeometry((uint)profile.Width, (uint)profile.Height));
                }

                if (!keepColor)
                    part.Grayscale(PixelIntensityMethod.Rec709Luma);
                var autoLevel = rendering.AutoContrast == ComicAutoContrast.All || (rendering.AutoContrast == ComicAutoContrast.BlackAndWhiteOnly && !keepColor);
                if (autoLevel)
                {
                    var range = part.Statistics().Composite();
                    if (range.Maximum - range.Minimum >= Quantum.Max * AutoLevelMinimumRange / 255.0)
                        part.AutoLevel();
                }

                part.Strip();
                part.Quality = (uint)rendering.JpegQuality;
                part.Format = MagickFormat.Jpeg;
                if (!keepColor)
                    part.ColorType = ColorType.Grayscale;

                return new RenderedPage { Jpeg = part.ToByteArray(), Width = (int)part.Width, Height = (int)part.Height, Kind = kind, IsColor = keepColor };
            }
        }

        /// <summary>
        /// KCC's margin crop: finds the bounding box of the page's content and crops to it plus
        /// <see cref="ComicRenderingSettings.PreserveMarginPercent"/>, capped at
        /// <see cref="MaxMarginCropPerSide"/> per side so art bleeding off the page is never cut deep.
        /// Unlike KCC, which guesses a white or black background and thresholds on darkness, the
        /// background is the dominant tone of the outer 2 % frame and content is anything that
        /// differs from it by more than the cropping strength's difference (measured on a
        /// <see cref="MarginAnalysisSize"/> copy, in gamma-encoded luma — in linear light dark tones
        /// merge). A page whose frame isn't mostly one tone (full-bleed art) is not cropped at all;
        /// how "mostly" is also set by the cropping strength.
        /// Unlike KCC, nothing near the edge is dismissed as scan noise: a footer or caption close to
        /// the edge looks exactly like noise to that rule and was cut off, so a stray edge mark now
        /// just leaves that side uncropped.
        /// </summary>
        public void CropMargins(IMagickImage<byte> page)
        {
            int pageWidth = (int)page.Width, pageHeight = (int)page.Height;
            if (pageWidth < 100 || pageHeight < 100)
                return;

            var scale = Math.Min(1.0, (double)MarginAnalysisSize / Math.Max(pageWidth, pageHeight));
            int width, height;
            byte[] luminance;
            using (var gray = page.Clone())
            {
                if (scale < 1.0)
                    gray.Scale(new MagickGeometry((uint)Math.Round(pageWidth * scale), (uint)Math.Round(pageHeight * scale)) { IgnoreAspectRatio = true });
                else
                    gray.Blur(1, 1);
                gray.Grayscale(PixelIntensityMethod.Rec709Luma);
                width = (int)gray.Width;
                height = (int)gray.Height;
                using var pixels = gray.GetPixelsUnsafe();
                luminance = pixels.ToByteArray("R")!;
            }

            var background = FindUniformFrameTone(luminance, width, height);
            if (background is null)
                return;

            var content = new bool[luminance.Length];
            for (var i = 0; i < luminance.Length; i++)
                content[i] = Math.Abs(luminance[i] - background.Value) > cropping.ContentDifference;

            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!content[y * width + x])
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (right < 0)
                return;

            var padX = (int)(pageWidth * rendering.PreserveMarginPercent / 100);
            var padY = (int)(pageHeight * rendering.PreserveMarginPercent / 100);
            left = Math.Min(Math.Max(0, (int)Math.Floor(left / scale) - padX), (int)(pageWidth * MaxMarginCropPerSide));
            top = Math.Min(Math.Max(0, (int)Math.Floor(top / scale) - padY), (int)(pageHeight * MaxMarginCropPerSide));
            right = Math.Max(Math.Min(pageWidth, (int)Math.Ceiling((right + 1) / scale) + padX), (int)(pageWidth * (1 - MaxMarginCropPerSide)));
            bottom = Math.Max(Math.Min(pageHeight, (int)Math.Ceiling((bottom + 1) / scale) + padY), (int)(pageHeight * (1 - MaxMarginCropPerSide)));
            if (left == 0 && top == 0 && right == pageWidth && bottom == pageHeight)
                return;

            page.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
            page.ResetPage();
        }

        /// <summary>
        /// The median luminance of the outer 2 % frame, or null when less than the cropping strength's
        /// uniformity share of the frame's pixels are close to it.
        /// </summary>
        private int? FindUniformFrameTone(byte[] luminance, int width, int height)
        {
            var band = Math.Max(1, Math.Min(width, height) / 50);
            var histogram = new int[256];
            var count = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (x >= band && x < width - band && y >= band && y < height - band)
                        continue;
                    histogram[luminance[y * width + x]]++;
                    count++;
                }
            }

            var median = 0;
            for (var seen = 0; median < 255 && seen + histogram[median] <= count / 2; median++)
                seen += histogram[median];

            var close = 0;
            for (var tone = Math.Max(0, median - cropping.ContentDifference); tone <= Math.Min(255, median + cropping.ContentDifference); tone++)
                close += histogram[tone];
            return close >= count * cropping.FrameUniformity ? median : null;
        }

        /// <summary>
        /// Samples the page down to at most 128 px and counts pixels whose channels spread more than
        /// <see cref="ColorChromaThreshold"/> apart. Grayscale scans with a sepia cast or JPEG chroma
        /// noise stay under <see cref="ColorPixelShare"/>; genuinely coloured art is far above it.
        /// </summary>
        private bool IsColorful(MagickImage image)
        {
            if (image.ColorSpace == ColorSpace.Gray || image.ChannelCount < 3)
                return false;

            using var sample = image.Clone();
            sample.Sample(new MagickGeometry(128, 128));
            using var pixels = sample.GetPixelsUnsafe();
            var rgb = pixels.ToByteArray(PixelMapping.RGB);
            if (rgb is null || rgb.Length == 0)
                return false;

            var colored = 0;
            for (var i = 0; i + 2 < rgb.Length; i += 3)
            {
                int r = rgb[i], g = rgb[i + 1], b = rgb[i + 2];
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > ColorChromaThreshold)
                    colored++;
            }
            return colored >= rgb.Length / 3 * ColorPixelShare;
        }
    }

    private enum PageKind
    {
        Single,
        SpreadFirst,
        SpreadSecond,
        Rotated
    }

    private sealed record RenderedPage
    {
        public byte[] Jpeg { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public PageKind Kind { get; init; }
        public bool IsColor { get; init; }
    }
}
