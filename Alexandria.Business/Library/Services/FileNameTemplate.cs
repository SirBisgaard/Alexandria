using System.Globalization;
using System.Text;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Library.Services;

/// <summary>
/// Pure template renderer for book filenames. Supports tokens <c>&lt;Author&gt;</c>,
/// <c>&lt;Title&gt;</c>, <c>&lt;Series&gt;</c>, <c>&lt;SeriesIndex&gt;</c> and optional groups
/// in parentheses that drop out when any inner token resolves empty.
/// </summary>
public static class FileNameTemplate
{
    public const int MaxBaseNameLength = 200;

    /// <summary>
    /// Default filename template seeded into the <c>Settings</c> table by the V2 migration
    /// and used as the fallback by <see cref="Render"/> when a book renders against it.
    /// Kept in sync with the literal in <c>DatabaseInitializer.MigrateV2_FileRenameTemplate</c>.
    /// </summary>
    public const string DefaultTemplate = "<Authors> - (<Serie> #<SerieIndex> - )<Title>";

    private static readonly HashSet<string> KnownTokens = new(StringComparer.Ordinal)
    {
        "Authors",
        "Title",
        "Serie",
        "SerieIndex"
    };

    /// <summary>
    /// Applies the same sanitization pipeline used by <see cref="Render"/> to an arbitrary
    /// filename base (no parsing). Strips invalid characters, collapses whitespace, and trims
    /// leading/trailing punctuation. Does not enforce the length budget — the caller chooses.
    /// </summary>
    public static string SanitizeFilename(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return SanitizeFinal(value);
    }

    /// <summary>
    /// Parses and validates a template without rendering against a book. Returns
    /// <see cref="TemplateValidation.Ok"/> if the template is syntactically valid
    /// and references only known tokens.
    /// </summary>
    public static TemplateValidation Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return TemplateValidation.Invalid("Template is empty.");

        try
        {
            Parse(template);
            return TemplateValidation.Ok;
        }
        catch (TemplateException ex)
        {
            return TemplateValidation.Invalid(ex.Message);
        }
    }

    /// <summary>
    /// Renders <paramref name="template"/> against the book's metadata. Throws
    /// <see cref="TemplateException"/> for malformed templates.
    /// </summary>
    public static string Render(Book book, string template)
    {
        var segments = Parse(template);
        var pieces = new List<Piece>();
        EvaluateSegments(segments, book, pieces, insideGroup: false);

        var assembled = AssembleAndSanitize(pieces);
        if (assembled.Length <= MaxBaseNameLength)
            return assembled;

        return TruncateRespectingTitle(pieces);
    }

    private static List<Segment> Parse(string template)
    {
        var index = 0;
        var segments = ParseSegments(template, ref index, stopAtCloseParen: false);
        if (index != template.Length)
            throw new TemplateException("Unexpected ')' without matching '('.");
        return segments;
    }

    private static List<Segment> ParseSegments(string template, ref int index, bool stopAtCloseParen)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();

        while (index < template.Length)
        {
            var c = template[index];

            if (c == '\\' && index + 1 < template.Length)
            {
                literal.Append(template[index + 1]);
                index += 2;
                continue;
            }

            if (c == ')')
            {
                if (!stopAtCloseParen)
                    throw new TemplateException("Unexpected ')' without matching '('.");
                FlushLiteral(segments, literal);
                index++;
                return segments;
            }

            if (c == '<')
            {
                FlushLiteral(segments, literal);
                var close = template.IndexOf('>', index + 1);
                if (close == -1)
                    throw new TemplateException("Unclosed token: missing '>'.");
                var name = template.Substring(index + 1, close - index - 1);
                if (name.Length == 0)
                    throw new TemplateException("Empty token '<>'.");
                if (!KnownTokens.Contains(name))
                    throw new TemplateException($"Unknown token <{name}>.");
                segments.Add(new TokenSegment(name));
                index = close + 1;
                continue;
            }

            if (c == '(')
            {
                FlushLiteral(segments, literal);
                if (stopAtCloseParen)
                    throw new TemplateException("Nested optional groups are not supported.");
                index++;
                var inner = ParseSegments(template, ref index, stopAtCloseParen: true);
                segments.Add(new GroupSegment(inner));
                continue;
            }

            literal.Append(c);
            index++;
        }

        if (stopAtCloseParen)
            throw new TemplateException("Unclosed optional group: missing ')'.");

        FlushLiteral(segments, literal);
        return segments;
    }

    private static void FlushLiteral(List<Segment> segments, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;
        segments.Add(new LiteralSegment(literal.ToString()));
        literal.Clear();
    }

    private static void EvaluateSegments(List<Segment> segments, Book book, List<Piece> output, bool insideGroup)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case LiteralSegment literal:
                    output.Add(new Piece(literal.Text, IsTitle: false));
                    break;
                case TokenSegment token:
                    var value = CollapseWhitespace(ResolveToken(token.Name, book));
                    output.Add(new Piece(value, IsTitle: token.Name == "Title"));
                    break;
                case GroupSegment group:
                    EvaluateGroup(group, book, output);
                    break;
            }
        }
    }

    private static void EvaluateGroup(GroupSegment group, Book book, List<Piece> output)
    {
        var inner = new List<Piece>();
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case LiteralSegment literal:
                    inner.Add(new Piece(literal.Text, IsTitle: false));
                    break;
                case TokenSegment token:
                    var value = CollapseWhitespace(ResolveToken(token.Name, book));
                    if (string.IsNullOrEmpty(value))
                        return;
                    inner.Add(new Piece(value, IsTitle: token.Name == "Title"));
                    break;
            }
        }
        output.AddRange(inner);
    }

    private static string ResolveToken(string name, Book book)
    {
        return name switch
        {
            "Authors" => string.Join(", ", book.Authors.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n))),
            "Title" => book.Title ?? string.Empty,
            "Serie" => book.Series?.Name ?? string.Empty,
            "SerieIndex" => FormatSerieIndex(book.Series?.Index),
            _ => throw new TemplateException($"Unknown token <{name}>.")
        };
    }

    private static string FormatSerieIndex(decimal? index)
    {
        if (index is null)
            return string.Empty;
        var s = index.Value.ToString(CultureInfo.InvariantCulture);
        if (!s.Contains('.'))
            return s;
        return s.TrimEnd('0').TrimEnd('.');
    }

    private static string AssembleAndSanitize(IEnumerable<Piece> pieces)
    {
        var joined = string.Concat(pieces.Select(p => p.Text));
        return SanitizeFinal(joined);
    }

    private static string SanitizeFinal(string raw)
    {
        var stripped = StripInvalidChars(raw);
        return TrimEdges(stripped);
    }

    private static readonly char[] CrossPlatformInvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static string StripInvalidChars(string value)
    {
        var platformInvalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
                continue;
            if (Array.IndexOf(platformInvalid, ch) >= 0)
                continue;
            if (Array.IndexOf(CrossPlatformInvalidChars, ch) >= 0)
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string CollapseWhitespace(string value)
    {
        var sb = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (previousWasSpace)
                    continue;
                sb.Append(' ');
                previousWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                previousWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string TrimEdges(string value)
    {
        var chars = new[] { ' ', '\t', '-', '.' };
        return value.Trim(chars);
    }

    private static string TruncateRespectingTitle(List<Piece> pieces)
    {
        var fixedLength = pieces.Where(p => !p.IsTitle).Sum(p => p.Text.Length);
        var budget = Math.Max(0, MaxBaseNameLength - fixedLength);

        var titlePieces = pieces.Where(p => p.IsTitle).ToList();
        if (titlePieces.Count == 0)
            return SanitizeFinal(string.Concat(pieces.Select(p => p.Text)))[..MaxBaseNameLength];

        var shrunk = new List<Piece>(pieces.Count);
        var remaining = budget;
        foreach (var piece in pieces)
        {
            if (!piece.IsTitle)
            {
                shrunk.Add(piece);
                continue;
            }
            var allowed = Math.Min(piece.Text.Length, remaining);
            shrunk.Add(new Piece(piece.Text[..allowed], IsTitle: true));
            remaining -= allowed;
        }

        var result = AssembleAndSanitize(shrunk);
        if (result.Length > MaxBaseNameLength)
            result = result[..MaxBaseNameLength];
        return result;
    }

    private sealed record Piece(string Text, bool IsTitle)
    {
    }

    private abstract record Segment
    {
    }

    private sealed record LiteralSegment(string Text) : Segment
    {
    }

    private sealed record TokenSegment(string Name) : Segment
    {
    }

    private sealed record GroupSegment(List<Segment> Children) : Segment
    {
    }
}

public sealed class TemplateException(string message) : Exception(message)
{
}

public readonly record struct TemplateValidation(bool IsValid, string? Error)
{
    public static TemplateValidation Ok => new(true, null);
    public static TemplateValidation Invalid(string reason) => new(false, reason);
}
